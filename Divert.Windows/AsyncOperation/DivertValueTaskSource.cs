using Windows.Win32;

namespace Divert.Windows.AsyncOperation;

internal abstract unsafe class DivertValueTaskSource : IDisposable
{
    private static class Status
    {
        public const uint Idle = 0;
        public const uint Canceled = 1;
        public const uint Pending = 2;
        public const uint Disposed = 3;
    }

    private static readonly IOCompletionCallback ioCompletionCallback = OnIOCompleted;

    private readonly DivertHandle divertHandle;
    private readonly ThreadPoolBoundHandle threadPoolBoundHandle;
    private readonly PreAllocatedOverlapped preAllocatedOverlapped;

    private IntPtr nativeOverlapped;
    private PendingOperation pendingOperation;
    private uint status;

    protected DivertHandle DivertHandle => divertHandle;

    public DivertValueTaskSource(DivertHandle divertHandle, ThreadPoolBoundHandle threadPoolBoundHandle)
    {
        this.divertHandle = divertHandle;
        this.threadPoolBoundHandle = threadPoolBoundHandle;
        preAllocatedOverlapped = new PreAllocatedOverlapped(ioCompletionCallback, this, null);
    }

    // From cancellation registration.
    private void ExecuteOrRequestCancel()
    {
        uint originalStatus = Interlocked.CompareExchange(ref status, Status.Canceled, Status.Idle);
        if (originalStatus is Status.Pending)
        {
            using (divertHandle.GetReference(out var handle))
            {
                _ = PInvoke.CancelIoEx(new(handle), pendingOperation.NativeOverlapped);
            }
        }
    }

    // After ERROR_IO_PENDING.
    protected void CancelIfRequested()
    {
        uint originalStatus = Interlocked.CompareExchange(ref status, Status.Pending, Status.Idle);
        if (originalStatus is Status.Canceled)
        {
            using (divertHandle.GetReference(out var handle))
            {
                _ = PInvoke.CancelIoEx(new(handle), pendingOperation.NativeOverlapped);
            }
        }
    }

    // From completion callback.
    private void ResetIfPendingOrCanceled()
    {
        uint originalStatus = Interlocked.CompareExchange(ref status, Status.Idle, Status.Pending);
        if (originalStatus is Status.Canceled)
        {
            Interlocked.CompareExchange(ref status, Status.Idle, originalStatus);
        }
    }

    protected PendingOperation PrepareOperation(
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
        var nativeOverlapped = threadPoolBoundHandle.AllocateNativeOverlapped(preAllocatedOverlapped);
        this.nativeOverlapped = new(nativeOverlapped);
        pendingOperation = new PendingOperation(
            nativeOverlapped,
            packetBuffer,
            addresses,
            cancellationTokenRegistration
        );
        return pendingOperation;
    }

    private void DisposePendingOperation()
    {
        var overlapped = Interlocked.Exchange(ref nativeOverlapped, default);
        if (overlapped != default)
        {
            pendingOperation.Dispose();
            threadPoolBoundHandle.FreeNativeOverlapped((NativeOverlapped*)overlapped);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref status, Status.Disposed) is Status.Disposed)
        {
            return;
        }

        DisposePendingOperation();
        preAllocatedOverlapped.Dispose();
        GC.SuppressFinalize(this);
    }

    ~DivertValueTaskSource()
    {
        Dispose();
    }

    protected abstract void Complete(uint errorCode, uint numBytes, in PendingOperation pendingOperation);

    private static void OnIOCompleted(uint errorCode, uint numBytes, NativeOverlapped* pOVERLAP)
    {
        var vts = (DivertValueTaskSource)ThreadPoolBoundHandle.GetNativeOverlappedState(pOVERLAP)!;
        try
        {
            vts.Complete(errorCode, numBytes, vts.pendingOperation);
            vts.ResetIfPendingOrCanceled();
        }
        finally
        {
            vts.DisposePendingOperation();
        }
    }
}
