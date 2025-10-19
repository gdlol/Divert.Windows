using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Windows.Win32.Foundation;

namespace Divert.Windows;

/// <summary>
/// Main entry point WinDivert APIs.
/// </summary>
public sealed unsafe class DivertService : IDisposable
{
    private readonly DivertHandle handle;

    private readonly ThreadPoolBoundHandle threadPoolBoundHandle;
    private readonly Channel<DivertValueTaskSource> vtsPool = Channel.CreateUnbounded<DivertValueTaskSource>();

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
        this.handle = new DivertHandle(handle);
        threadPoolBoundHandle = ThreadPoolBoundHandle.BindHandle(this.handle);
    }

    private bool disposed = false;
    private bool closed = false;

    private void Close(bool throwOnError)
    {
        if (!closed)
        {
            bool success = NativeMethods.WinDivertClose(handle.Handle);
            if (!success && throwOnError)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
            closed = true;
        }
    }

    /// <summary>
    /// Closes the WinDivert handle.
    /// </summary>
    public void Close() => Close(throwOnError: true);

    /// <summary>
    /// Releases all resources used by the <see cref="DivertService"/>.
    /// </summary>
    public void Dispose()
    {
        if (!disposed)
        {
            if (vtsPool.Writer.TryComplete())
            {
                while (vtsPool.Reader.TryRead(out var vts))
                {
                    vts.Dispose();
                }
            }
            threadPoolBoundHandle.Dispose();
            handle.Dispose();
            Close(throwOnError: false);
            disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    ~DivertService()
    {
        Dispose();
    }

    private void ThrowIfClosedOrDisposed()
    {
        if (closed)
        {
            throw new InvalidOperationException(nameof(closed));
        }
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    /// <summary>
    /// Shuts down the WinDivert handle.
    /// </summary>
    /// <param name="how">Specifies how to shut down the handle.</param>
    public void Shutdown(DivertShutdown how)
    {
        ThrowIfClosedOrDisposed();

        bool success = NativeMethods.WinDivertShutdown(handle.Handle, (WINDIVERT_SHUTDOWN)how);
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
        ThrowIfClosedOrDisposed();

        if (!vtsPool.Reader.TryRead(out var vts))
        {
            vts = new DivertValueTaskSource(
                vtsPool,
                handle.Handle,
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
        ThrowIfClosedOrDisposed();

        if (!vtsPool.Reader.TryRead(out var vts))
        {
            vts = new DivertValueTaskSource(
                vtsPool,
                handle.Handle,
                threadPoolBoundHandle,
                runContinuationsAsynchronously: true
            );
        }
        return vts.SendAsync(buffer, addresses, cancellationToken);
    }
}
