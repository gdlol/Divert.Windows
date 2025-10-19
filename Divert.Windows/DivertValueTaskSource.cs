using System.Buffers;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using System.Threading.Tasks.Sources;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Divert.Windows;

internal sealed unsafe class DivertValueTaskSource
    : IValueTaskSource<DivertReceiveResult>,
        IValueTaskSource,
        IDisposable
{
    private static class Status
    {
        public const uint Idle = 0;
        public const uint Canceled = 1;
        public const uint Pending = 2;
        public const uint Disposed = 3;
    }

    private static readonly IOCompletionCallback ioCompletionCallback = OnIOCompleted;

    private ManualResetValueTaskSourceCore<DivertReceiveResult> source;
    private readonly Channel<DivertValueTaskSource> pool;
    private readonly IntPtr divertHandle;
    private readonly ThreadPoolBoundHandle threadPoolBoundHandle;
    private readonly PreAllocatedOverlapped preAllocatedOverlapped;
    private readonly Memory<uint> addressesLengthBuffer;

    private struct PendingOperation(
        DivertValueTaskSource vts,
        Memory<byte> packetBuffer,
        Memory<DivertAddress> addresses,
        CancellationTokenRegistration cancellationTokenRegistration
    ) : IDisposable
    {
        public NativeOverlapped* NativeOverlapped { get; private set; } =
            vts.threadPoolBoundHandle.AllocateNativeOverlapped(vts.preAllocatedOverlapped);

        public readonly Memory<DivertAddress> Addresses => addresses;

        public MemoryHandle PacketBufferHandle { get; } = packetBuffer.Pin();
        public MemoryHandle AddressesHandle { get; } = addresses.Pin();
        public MemoryHandle AddressesLengthBufferHandle { get; } = vts.addressesLengthBuffer.Pin();

        public readonly CancellationToken CancellationToken => cancellationTokenRegistration.Token;

        public void Dispose()
        {
            cancellationTokenRegistration.Dispose();

            PacketBufferHandle.Dispose();
            AddressesHandle.Dispose();
            AddressesLengthBufferHandle.Dispose();

            if (NativeOverlapped is not null)
            {
                vts.threadPoolBoundHandle.FreeNativeOverlapped(NativeOverlapped);
                NativeOverlapped = null;
            }
        }
    }

    private PendingOperation pendingOperation;
    private uint status;

    public DivertValueTaskSource(
        Channel<DivertValueTaskSource> pool,
        IntPtr divertHandle,
        ThreadPoolBoundHandle threadPoolBoundHandle,
        bool runContinuationsAsynchronously
    )
    {
        source.RunContinuationsAsynchronously = runContinuationsAsynchronously;
        this.pool = pool;
        this.divertHandle = divertHandle;
        this.threadPoolBoundHandle = threadPoolBoundHandle;
        preAllocatedOverlapped = new PreAllocatedOverlapped(ioCompletionCallback, this, null);
        addressesLengthBuffer = GC.AllocateArray<uint>(1, pinned: true);
    }

    private void ExecuteOrRequestCancel()
    {
        uint originalStatus = Interlocked.CompareExchange(ref status, Status.Canceled, Status.Idle);
        if (originalStatus is Status.Pending)
        {
            _ = PInvoke.CancelIoEx(new(divertHandle), pendingOperation.NativeOverlapped);
        }
    }

    private void CancelIfRequested()
    {
        uint originalStatus = Interlocked.CompareExchange(ref status, Status.Pending, Status.Idle);
        if (originalStatus is Status.Canceled)
        {
            _ = PInvoke.CancelIoEx(new(divertHandle), pendingOperation.NativeOverlapped);
            Interlocked.CompareExchange(ref status, Status.Idle, originalStatus);
        }
    }

    private PendingOperation PrepareOperation(
        Memory<byte> packetBuffer,
        Memory<DivertAddress> addresses,
        CancellationToken cancellationToken
    )
    {
        var cancellationTokenRegistration = cancellationToken.CanBeCanceled
            ? cancellationToken.UnsafeRegister(
                static state => ((DivertValueTaskSource)state!).ExecuteOrRequestCancel(),
                this
            )
            : default;
        pendingOperation = new PendingOperation(this, packetBuffer, addresses, cancellationTokenRegistration);
        return pendingOperation;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref status, Status.Disposed) is Status.Disposed)
        {
            return;
        }

        pendingOperation.Dispose();
        preAllocatedOverlapped.Dispose();
        GC.SuppressFinalize(this);
    }

    ~DivertValueTaskSource()
    {
        Dispose();
    }

    internal short Version => source.Version;

    private void Complete(uint errorCode, uint numBytes)
    {
        var addresses = pendingOperation.Addresses;
        int addressesLength = (int)addressesLengthBuffer.Span[0] / sizeof(DivertAddress);
        var token = pendingOperation.CancellationToken;
        pendingOperation.Dispose();

        if (errorCode == (uint)WIN32_ERROR.ERROR_NO_DATA)
        {
            errorCode = 0;
        }

        if (errorCode == (uint)WIN32_ERROR.ERROR_SUCCESS)
        {
            source.SetResult(new DivertReceiveResult((int)numBytes, addresses[..addressesLength]));
        }
        else if (errorCode == (uint)WIN32_ERROR.ERROR_OPERATION_ABORTED)
        {
            var exception = token.IsCancellationRequested
                ? new OperationCanceledException()
                : new OperationCanceledException(token);
            source.SetException(exception);
        }
        else
        {
            var exception = new Win32Exception((int)errorCode);
            source.SetException(exception);
        }
    }

    private static void OnIOCompleted(uint errorCode, uint numBytes, NativeOverlapped* pOVERLAP)
    {
        var vts = (DivertValueTaskSource)ThreadPoolBoundHandle.GetNativeOverlappedState(pOVERLAP)!;
        vts.Complete(errorCode, numBytes);
    }

    public ValueTaskSourceStatus GetStatus(short token) => source.GetStatus(token);

    public void OnCompleted(
        Action<object?> continuation,
        object? state,
        short token,
        ValueTaskSourceOnCompletedFlags flags
    ) => source.OnCompleted(continuation, state, token, flags);

    public DivertReceiveResult GetResult(short token)
    {
        try
        {
            return source.GetResult(token);
        }
        finally
        {
            Interlocked.CompareExchange(ref status, Status.Idle, Status.Pending);
            source.Reset();
            if (!pool.Writer.TryWrite(this))
            {
                Dispose();
            }
        }
    }

    void IValueTaskSource.GetResult(short token) => GetResult(token);

    public ValueTask<DivertReceiveResult> ReceiveAsync(
        Memory<byte> buffer,
        Memory<DivertAddress> addresses,
        CancellationToken cancellationToken
    )
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled<DivertReceiveResult>(cancellationToken);
        }

        addressesLengthBuffer.Span[0] = (uint)(addresses.Length * sizeof(DivertAddress));
        var pendingOperation = PrepareOperation(buffer, addresses, cancellationToken);
        bool success = NativeMethods.WinDivertRecvEx(
            divertHandle,
            pendingOperation.PacketBufferHandle.Pointer,
            (uint)buffer.Length,
            null,
            0,
            (WINDIVERT_ADDRESS*)pendingOperation.AddressesHandle.Pointer,
            (uint*)pendingOperation.AddressesLengthBufferHandle.Pointer,
            pendingOperation.NativeOverlapped
        );
        if (!success)
        {
            int error = Marshal.GetLastPInvokeError();
            if (error == (int)WIN32_ERROR.ERROR_IO_PENDING)
            {
                CancelIfRequested();
            }
            else
            {
                pendingOperation.Dispose();
                Interlocked.CompareExchange(ref status, Status.Idle, Status.Canceled);
                return ValueTask.FromException<DivertReceiveResult>(new Win32Exception(error));
            }
        }

        return new ValueTask<DivertReceiveResult>(this, source.Version);
    }

    public ValueTask SendAsync(
        ReadOnlyMemory<byte> buffer,
        ReadOnlyMemory<DivertAddress> addresses,
        CancellationToken cancellationToken
    )
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled(cancellationToken);
        }

        var pendingOperation = PrepareOperation(
            MemoryMarshal.AsMemory(buffer),
            MemoryMarshal.AsMemory(addresses),
            cancellationToken
        );
        bool success = NativeMethods.WinDivertSendEx(
            divertHandle,
            pendingOperation.PacketBufferHandle.Pointer,
            (uint)buffer.Length,
            null,
            0,
            (WINDIVERT_ADDRESS*)pendingOperation.AddressesHandle.Pointer,
            (uint)(addresses.Length * sizeof(DivertAddress)),
            pendingOperation.NativeOverlapped
        );
        if (!success)
        {
            int error = Marshal.GetLastPInvokeError();
            if (error == (int)WIN32_ERROR.ERROR_IO_PENDING)
            {
                CancelIfRequested();
            }
            else
            {
                pendingOperation.Dispose();
                Interlocked.CompareExchange(ref status, Status.Idle, Status.Canceled);
                return ValueTask.FromException(new Win32Exception(error));
            }
        }

        return new ValueTask(this, source.Version);
    }
}
