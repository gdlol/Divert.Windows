using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Divert.Windows.AsyncOperation;
using Windows.Win32.Foundation;

namespace Divert.Windows;

/// <summary>
/// Main entry point WinDivert APIs.
/// </summary>
public sealed unsafe class DivertService : IDisposable
{
    private readonly DivertHandle divertHandle;
    private readonly bool runContinuationsAsynchronously;

    private readonly ThreadPoolBoundHandle threadPoolBoundHandle;
    private readonly Channel<DivertValueTaskSource> sendVtsPool;
    private readonly Channel<DivertValueTaskSource> receiveVtsPool;
    private readonly DivertReceiveExecutor receiveExecutor;
    private readonly DivertSendExecutor sendExecutor;

    /// <summary>
    /// Opens a WinDivert handle for the given filter.
    /// </summary>
    /// <param name="filter">A packet filter string specified in the WinDivert filter language.</param>
    /// <param name="layer">The layer.</param>
    /// <param name="priority">The priority of the handle.</param>
    /// <param name="flags">Additional flags.</param>
    public DivertService(
        DivertFilter filter,
        DivertLayer layer = DivertLayer.Network,
        short priority = 0,
        DivertFlags flags = DivertFlags.None,
        bool runContinuationsAsynchronously = true
    )
    {
        ArgumentNullException.ThrowIfNull(filter);

        using var s = new CString(filter.Clause);
        IntPtr errorStr;
        uint errorPos;
        bool success = NativeMethods.WinDivertHelperCompileFilter(
            s.Ptr,
            (WINDIVERT_LAYER)layer,
            null,
            0,
            &errorStr,
            &errorPos
        );
        if (!success)
        {
            string? errorString = Marshal.PtrToStringAnsi(errorStr);
            throw new ArgumentException(
                $"{errorString} ({errorPos}): ...{filter.Clause[(int)errorPos..]}",
                nameof(filter)
            );
        }

        var handle = NativeMethods.WinDivertOpen(s.Ptr, (WINDIVERT_LAYER)layer, priority, (ulong)flags);
        if (new HANDLE(handle) == HANDLE.INVALID_HANDLE_VALUE)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        divertHandle = new DivertHandle(handle);
        this.runContinuationsAsynchronously = runContinuationsAsynchronously;
        threadPoolBoundHandle = ThreadPoolBoundHandle.BindHandle(divertHandle);
        sendVtsPool = Channel.CreateUnbounded<DivertValueTaskSource>();
        receiveVtsPool = Channel.CreateUnbounded<DivertValueTaskSource>();
        receiveExecutor = new DivertReceiveExecutor();
        sendExecutor = new DivertSendExecutor();
    }

    public DivertService(SafeHandle handle, bool runContinuationsAsynchronously = true)
    {
        ArgumentNullException.ThrowIfNull(handle);

        using var _ = handle.DangerousGetHandle(out var nativeHandle);
        divertHandle = new DivertHandle(nativeHandle, ownsHandle: false);
        this.runContinuationsAsynchronously = runContinuationsAsynchronously;
        threadPoolBoundHandle = ThreadPoolBoundHandle.BindHandle(divertHandle);
        sendVtsPool = Channel.CreateUnbounded<DivertValueTaskSource>();
        receiveVtsPool = Channel.CreateUnbounded<DivertValueTaskSource>();
        receiveExecutor = new DivertReceiveExecutor();
        sendExecutor = new DivertSendExecutor();
    }

    private bool disposed = false;

    private static void DisposeVtsPool(Channel<DivertValueTaskSource> pool)
    {
        pool.Writer.TryComplete();
        while (pool.Reader.TryRead(out var vts))
        {
            vts.Dispose();
        }
    }

    /// <summary>
    /// Releases all resources used by the <see cref="DivertService"/>.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;

        DisposeVtsPool(sendVtsPool);
        DisposeVtsPool(receiveVtsPool);
        threadPoolBoundHandle.Dispose();
        divertHandle.Dispose();
    }

    private DivertValueTaskSource GetVts(Channel<DivertValueTaskSource> vtsPool)
    {
        if (!vtsPool.Reader.TryRead(out var vts))
        {
            vts = new DivertValueTaskSource(
                sendVtsPool,
                divertHandle,
                threadPoolBoundHandle,
                runContinuationsAsynchronously
            );
        }
        return vts;
    }

    /// <summary>
    /// Receives one or more packets from the network stack.
    /// </summary>
    /// <param name="buffer">Buffer to receive the packet data.</param>
    /// <param name="addresses">Buffer to receive the packet addresses.</param>
    /// <param name="cancellationToken">Token to observe cancellation requests.</param>
    /// <returns>A ValueTask representing the asynchronous receive operation.</returns>
    public ValueTask<DivertReceiveResult> ReceiveAsync(
        Memory<byte> buffer,
        Memory<DivertAddress> addresses,
        CancellationToken cancellationToken = default
    )
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled<DivertReceiveResult>(cancellationToken);
        }

        var vts = GetVts(receiveVtsPool);
        return receiveExecutor.ReceiveAsync(vts, buffer, addresses, cancellationToken);
    }

    /// <summary>
    /// Injects one or more packets into the network stack.
    /// </summary>
    /// <param name="buffer">Buffer containing the packet data.</param>
    /// <param name="addresses">Addresses of the packets to be injected.</param>
    /// <param name="cancellationToken">Token to observe cancellation requests.</param>
    /// <returns>A ValueTask representing the asynchronous send operation.</returns>
    public ValueTask<int> SendAsync(
        ReadOnlyMemory<byte> buffer,
        ReadOnlyMemory<DivertAddress> addresses,
        CancellationToken cancellationToken = default
    )
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled<int>(cancellationToken);
        }

        var vts = GetVts(sendVtsPool);
        return sendExecutor.SendAsync(vts, buffer, addresses, cancellationToken);
    }
}
