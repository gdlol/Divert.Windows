using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using System.Threading.Tasks.Sources;
using Windows.Win32.Foundation;

namespace Divert.Windows.AsyncOperation;

internal sealed unsafe class DivertReceiveValueTaskSource(
    Channel<DivertReceiveValueTaskSource> pool,
    DivertHandle divertHandle,
    ThreadPoolBoundHandle threadPoolBoundHandle,
    bool runContinuationsAsynchronously
) : DivertValueTaskSource(divertHandle, threadPoolBoundHandle), IValueTaskSource<DivertReceiveResult>
{
    private ManualResetValueTaskSourceCore<DivertReceiveResult> source = new()
    {
        RunContinuationsAsynchronously = runContinuationsAsynchronously,
    };

    private readonly Memory<uint> addressesLengthBuffer = GC.AllocateArray<uint>(1, pinned: true);

    protected override void Complete(uint errorCode, uint numBytes, in PendingOperation pendingOperation)
    {
        var addresses = pendingOperation.Addresses;
        int addressesLength = (int)addressesLengthBuffer.Span[0] / sizeof(DivertAddress);
        var token = pendingOperation.CancellationToken;

        if (errorCode is (uint)WIN32_ERROR.ERROR_NO_DATA)
        {
            errorCode = 0;
        }

        if (errorCode is (uint)WIN32_ERROR.ERROR_SUCCESS)
        {
            source.SetResult(new DivertReceiveResult((int)numBytes, addresses[..addressesLength]));
        }
        else if (errorCode is (uint)WIN32_ERROR.ERROR_OPERATION_ABORTED)
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

    public DivertReceiveResult GetResult(short token)
    {
        try
        {
            return source.GetResult(token);
        }
        finally
        {
            source.Reset();
            if (!pool.Writer.TryWrite(this))
            {
                Dispose();
            }
        }
    }

    public ValueTaskSourceStatus GetStatus(short token) => source.GetStatus(token);

    public void OnCompleted(
        Action<object?> continuation,
        object? state,
        short token,
        ValueTaskSourceOnCompletedFlags flags
    ) => source.OnCompleted(continuation, state, token, flags);

    public ValueTask<DivertReceiveResult> ReceiveAsync(
        Memory<byte> buffer,
        Memory<DivertAddress> addresses,
        CancellationToken cancellationToken
    )
    {
        using var _ = DivertHandle.GetReference(out var handle);
        addressesLengthBuffer.Span[0] = (uint)(addresses.Length * sizeof(DivertAddress));
        var pendingOperation = PrepareOperation(buffer, addresses, cancellationToken);
        try
        {
            bool success = NativeMethods.WinDivertRecvEx(
                handle,
                pendingOperation.PacketBufferHandle.Pointer,
                (uint)buffer.Length,
                null,
                0,
                (WINDIVERT_ADDRESS*)pendingOperation.AddressesHandle.Pointer,
                (uint*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(addressesLengthBuffer.Span)),
                pendingOperation.NativeOverlapped
            );
            if (!success)
            {
                int error = Marshal.GetLastPInvokeError();
                if (error is (int)WIN32_ERROR.ERROR_IO_PENDING)
                {
                    CancelIfRequested();
                }
                else
                {
                    Dispose();
                    return ValueTask.FromException<DivertReceiveResult>(new Win32Exception(error));
                }
            }

            return new ValueTask<DivertReceiveResult>(this, source.Version);
        }
        catch
        {
            Dispose();
            throw;
        }
    }
}
