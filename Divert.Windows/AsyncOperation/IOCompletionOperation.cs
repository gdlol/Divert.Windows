using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Divert.Windows.AsyncOperation;

internal interface IOCompletionHandler
{
    void OnCompleted(uint errorCode, uint numBytes);
}

internal sealed unsafe class IOCompletionOperation<THandler> : IDisposable, IOCompletionHandler
    where THandler : IOCompletionHandler
{
    private static void OnIOCompleted(uint errorCode, uint numBytes, NativeOverlapped* pOVERLAP)
    {
        var operation = (IOCompletionOperation<THandler>)ThreadPoolBoundHandle.GetNativeOverlappedState(pOVERLAP)!;
        operation.OnCompleted(errorCode, numBytes);
    }

    private static readonly IOCompletionCallback ioCompletionCallback = OnIOCompleted;

    private readonly ThreadPoolBoundHandle threadPoolBoundHandle;
    private readonly CancellationHandle cancellationHandle;
    private readonly PreAllocatedOverlapped preAllocatedOverlapped;
    private readonly THandler handler;

    private NativeOverlapped* nativeOverlapped;

    public IOCompletionOperation(SafeHandle safeHandle, ThreadPoolBoundHandle threadPoolBoundHandle, THandler handler)
    {
        cancellationHandle = new CancellationHandle(safeHandle);
        this.threadPoolBoundHandle = threadPoolBoundHandle;
        this.handler = handler;
        preAllocatedOverlapped = new PreAllocatedOverlapped(ioCompletionCallback, this, null);
    }

    public SafeHandle SafeHandle => cancellationHandle.SafeHandle;

    public void Dispose()
    {
        if (nativeOverlapped is not null)
        {
            threadPoolBoundHandle.FreeNativeOverlapped(nativeOverlapped);
            nativeOverlapped = null;
        }
        preAllocatedOverlapped.Dispose();
        cancellationHandle.Dispose();
    }

    public CancellationTokenRegistration Prepare(CancellationToken cancellationToken, out NativeOverlapped* overlapped)
    {
        Debug.Assert(nativeOverlapped is null);
        nativeOverlapped = threadPoolBoundHandle.AllocateNativeOverlapped(preAllocatedOverlapped);
        var registration = cancellationToken.CanBeCanceled
            ? cancellationToken.UnsafeRegister(
                static state =>
                {
                    var operation = (IOCompletionOperation<THandler>)state!;
                    operation.cancellationHandle.RequestOrInvokeCancel(operation.nativeOverlapped);
                },
                this
            )
            : default;
        overlapped = nativeOverlapped;
        return registration;
    }

    public void CancelWhenRequested()
    {
        cancellationHandle.CancelWhenRequested(nativeOverlapped);
    }

    public void OnCompleted(uint errorCode, uint numBytes)
    {
        try
        {
            handler.OnCompleted(errorCode, numBytes);
        }
        finally
        {
            cancellationHandle.Reset();
            threadPoolBoundHandle.FreeNativeOverlapped(nativeOverlapped);
            nativeOverlapped = null;
        }
    }
}
