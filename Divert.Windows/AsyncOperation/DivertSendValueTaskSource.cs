using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using System.Threading.Tasks.Sources;
using Windows.Win32.Foundation;

namespace Divert.Windows.AsyncOperation;

internal sealed unsafe class DivertSendValueTaskSource(
    Channel<DivertSendValueTaskSource> pool,
    DivertHandle divertHandle,
    ThreadPoolBoundHandle threadPoolBoundHandle,
    bool runContinuationsAsynchronously
) : DivertValueTaskSource(divertHandle, threadPoolBoundHandle), IValueTaskSource
{
    private ManualResetValueTaskSourceCore<int> source = new()
    {
        RunContinuationsAsynchronously = runContinuationsAsynchronously,
    };

    protected override void Complete(uint errorCode, uint numBytes, in PendingOperation pendingOperation)
    {
        var token = pendingOperation.CancellationToken;

        if (errorCode is (uint)WIN32_ERROR.ERROR_SUCCESS)
        {
            source.SetResult(0);
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

    public void GetResult(short token)
    {
        try
        {
            source.GetResult(token);
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

    public ValueTask SendAsync(
        ReadOnlyMemory<byte> buffer,
        ReadOnlyMemory<DivertAddress> addresses,
        CancellationToken cancellationToken
    )
    {
        using var _ = DivertHandle.GetReference(out var handle);
        var pendingOperation = PrepareOperation(
            MemoryMarshal.AsMemory(buffer),
            MemoryMarshal.AsMemory(addresses),
            cancellationToken
        );
        try
        {
            bool success = NativeMethods.WinDivertSendEx(
                handle,
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
                    Dispose();
                    return ValueTask.FromException(new Win32Exception(error));
                }
            }

            return new ValueTask(this, source.Version);
        }
        catch
        {
            Dispose();
            throw;
        }
    }
}
