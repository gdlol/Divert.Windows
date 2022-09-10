using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;

[assembly: SupportedOSPlatform("windows6.0.6000")]

namespace Divert.Windows;

unsafe sealed public class DivertService : IDisposable
{
    internal HANDLE Handle { get; }

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
        DivertFlags flags = DivertFlags.None)
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
            &errorPos);
        if (!success)
        {
            string? errorString = Marshal.PtrToStringAnsi(errorStr);
            throw new ArgumentException($"{errorPos}: {errorString}", nameof(filter));
        }

        var handle = NativeMethods.WinDivertOpen(s.Ptr, (WINDIVERT_LAYER)layer, priority, (ulong)flags);
        if (handle.Value.ToInt64() == -1)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        Handle = handle;
    }

    private bool disposed = false;

    public void Dispose()
    {
        if (!disposed)
        {
            bool success = NativeMethods.WinDivertClose(Handle);
            if (!success)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    ~DivertService()
    {
        Dispose();
    }

    internal void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(DivertService));
        }
    }

    public void Shutdown(DivertShutdown how)
    {
        ThrowIfDisposed();

        bool success = NativeMethods.WinDivertShutdown(Handle, (WINDIVERT_SHUTDOWN)how);
        if (!success)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public (int packetLength, DivertAddress address) Receive(Span<byte> buffer)
    {
        ThrowIfDisposed();

        uint packetLength;
        WINDIVERT_ADDRESS address;
        fixed (byte* pBuffer = buffer)
        {
            bool success = NativeMethods.WinDivertRecv(
                Handle,
                pBuffer, checked((uint)buffer.Length),
                &packetLength,
                &address);
            if (!success)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        return ((int)packetLength, new DivertAddress(address));
    }

    public (int packetLength, int addressLength) ReceiveEx(
        Span<byte> buffer,
        Span<DivertAddress> addresses,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        using var eventHandle = new ManualResetEvent(initialState: false);
        using var _ = cancellationToken.Register(() => eventHandle.Set());
        var overlapped = new NativeOverlapped
        {
            EventHandle = eventHandle.SafeWaitHandle.DangerousGetHandle()
        };
        uint addrLen = (uint)(sizeof(DivertAddress) * addresses.Length);
        fixed (byte* pBuffer = buffer)
        fixed (DivertAddress* pAddr = addresses)
        {
            bool success = NativeMethods.WinDivertRecvEx(
                Handle,
                pBuffer, checked((uint)buffer.Length),
                null,
                0,
                (WINDIVERT_ADDRESS*)pAddr,
                &addrLen,
                &overlapped);
            if (!success)
            {
                int error = Marshal.GetLastWin32Error();
                if (error == 997) // ERROR_IO_PENDING
                {
                    eventHandle.WaitOne();
                    if (cancellationToken.IsCancellationRequested)
                    {
                        success = PInvoke.CancelIoEx(Handle, &overlapped);
                        if (!success)
                        {
                            error = Marshal.GetLastWin32Error();
                            if (error != 1168) // ERROR_NOT_FOUND
                            {
                                throw new Win32Exception(error);
                            }
                        }
                    }
                }
                else
                {
                    throw new Win32Exception(error);
                }
            }
            uint packetsLength;
            success = PInvoke.GetOverlappedResult(Handle, &overlapped, &packetsLength, true);
            if (!success)
            {
                int error = Marshal.GetLastWin32Error();
                if (error == 995) // ERROR_OPERATION_ABORTED
                {
                    throw new OperationCanceledException();
                }
                else
                {
                    throw new Win32Exception(error);
                }
            }
            int addressLength = (int)addrLen / sizeof(DivertAddress);
            return ((int)packetsLength, addressLength);
        }
    }

    public void Send(ReadOnlySpan<byte> buffer, DivertAddress address)
    {
        ThrowIfDisposed();

        var divertAddress = address.Struct;
        fixed (byte* pBuffer = buffer)
        {
            bool success = NativeMethods.WinDivertSend(
                Handle,
                pBuffer, checked((uint)buffer.Length),
                null,
                &divertAddress);
            if (!success)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
    }

    public void SendEx(
        ReadOnlySpan<byte> buffer,
        Span<DivertAddress> addresses,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        using var eventHandle = new ManualResetEvent(initialState: false);
        using var _ = cancellationToken.Register(() => eventHandle.Set());
        var overlapped = new NativeOverlapped
        {
            EventHandle = eventHandle.SafeWaitHandle.DangerousGetHandle()
        };
        fixed (byte* pBuffer = buffer)
        fixed (DivertAddress* pAddr = addresses)
        {
            bool success = NativeMethods.WinDivertSendEx(
                Handle,
                pBuffer, checked((uint)buffer.Length),
                null,
                0,
                (WINDIVERT_ADDRESS*)pAddr,
                (uint)(sizeof(WINDIVERT_ADDRESS) * addresses.Length),
                &overlapped);
            if (!success)
            {
                int error = Marshal.GetLastWin32Error();
                if (error == 997) // ERROR_IO_PENDING
                {
                    eventHandle.WaitOne();
                    if (cancellationToken.IsCancellationRequested)
                    {
                        success = PInvoke.CancelIoEx(Handle, &overlapped);
                        if (!success)
                        {
                            error = Marshal.GetLastWin32Error();
                            if (error != 1168) // ERROR_NOT_FOUND
                            {
                                throw new Win32Exception(error);
                            }
                        }
                    }
                }
                else
                {
                    throw new Win32Exception(error);
                }
            }
            uint bytesSent;
            success = PInvoke.GetOverlappedResult(Handle, &overlapped, &bytesSent, true);
            if (!success)
            {
                int error = Marshal.GetLastWin32Error();
                if (error == 995) // ERROR_OPERATION_ABORTED
                {
                    throw new OperationCanceledException();
                }
                else
                {
                    throw new Win32Exception(error);
                }
            }
        }
    }
}
