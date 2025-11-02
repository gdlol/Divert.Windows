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

    private readonly ThreadPoolBoundHandle threadPoolBoundHandle;
    private readonly Channel<DivertReceiveValueTaskSource> receiveVtsPool;
    private readonly Channel<DivertSendValueTaskSource> sendVtsPool;

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
        DivertFlags flags = DivertFlags.None
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
            throw new ArgumentException($"{errorPos}: {errorString}", nameof(filter));
        }

        var handle = NativeMethods.WinDivertOpen(s.Ptr, (WINDIVERT_LAYER)layer, priority, (ulong)flags);
        if (new HANDLE(handle) == HANDLE.INVALID_HANDLE_VALUE)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        divertHandle = new DivertHandle(handle);
        threadPoolBoundHandle = ThreadPoolBoundHandle.BindHandle(divertHandle);
        receiveVtsPool = Channel.CreateUnbounded<DivertReceiveValueTaskSource>();
        sendVtsPool = Channel.CreateUnbounded<DivertSendValueTaskSource>();
    }

    private bool disposed = false;

    private static void DisposeVtsPool<T>(Channel<T> vtsPool)
        where T : IDisposable
    {
        vtsPool.Writer.TryComplete();
        while (vtsPool.Reader.TryRead(out var vts))
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

        DisposeVtsPool(receiveVtsPool);
        DisposeVtsPool(sendVtsPool);
        threadPoolBoundHandle.Dispose();
        divertHandle.Dispose();
        GC.SuppressFinalize(this);
    }

    ~DivertService()
    {
        Dispose();
    }

    /// <summary>
    /// Shuts down the WinDivert handle.
    /// </summary>
    /// <param name="how">Specifies how to shut down the handle.</param>
    public void Shutdown(DivertShutdown how)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        using var _ = divertHandle.GetReference(out var handle);
        bool success = NativeMethods.WinDivertShutdown(handle, (WINDIVERT_SHUTDOWN)how);
        if (!success)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
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

        if (!receiveVtsPool.Reader.TryRead(out var vts))
        {
            vts = new DivertReceiveValueTaskSource(
                receiveVtsPool,
                divertHandle,
                threadPoolBoundHandle,
                runContinuationsAsynchronously: true
            );
        }
        return vts.ReceiveAsync(buffer, addresses, cancellationToken);
    }

    /// <summary>
    /// Injects one or more packets into the network stack.
    /// </summary>
    /// <param name="buffer">Buffer containing the packet data.</param>
    /// <param name="addresses">Addresses of the packets to be injected.</param>
    /// <param name="cancellationToken">Token to observe cancellation requests.</param>
    /// <returns>A ValueTask representing the asynchronous send operation.</returns>
    public ValueTask SendAsync(
        ReadOnlyMemory<byte> buffer,
        ReadOnlyMemory<DivertAddress> addresses,
        CancellationToken cancellationToken = default
    )
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled(cancellationToken);
        }

        if (!sendVtsPool.Reader.TryRead(out var vts))
        {
            vts = new DivertSendValueTaskSource(
                sendVtsPool,
                divertHandle,
                threadPoolBoundHandle,
                runContinuationsAsynchronously: true
            );
        }
        return vts.SendAsync(buffer, addresses, cancellationToken);
    }
}
