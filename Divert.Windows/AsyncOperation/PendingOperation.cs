using System.Buffers;

namespace Divert.Windows.AsyncOperation;

internal readonly unsafe struct PendingOperation(
    NativeOverlapped* nativeOverlapped,
    Memory<byte> packetBuffer,
    Memory<DivertAddress> addresses,
    CancellationTokenRegistration cancellationTokenRegistration
) : IDisposable
{
    public NativeOverlapped* NativeOverlapped { get; } = nativeOverlapped;
    public MemoryHandle PacketBufferHandle { get; } = packetBuffer.Pin();
    public MemoryHandle AddressesHandle { get; } = addresses.Pin();

    public readonly Memory<DivertAddress> Addresses => addresses;
    public readonly CancellationToken CancellationToken => cancellationTokenRegistration.Token;

    public void Dispose()
    {
        cancellationTokenRegistration.Dispose();
        PacketBufferHandle.Dispose();
        AddressesHandle.Dispose();
    }
}
