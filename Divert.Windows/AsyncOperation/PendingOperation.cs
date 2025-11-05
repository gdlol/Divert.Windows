using System.Buffers;

namespace Divert.Windows.AsyncOperation;

internal unsafe struct PendingOperation(
    NativeOverlapped* nativeOverlapped,
    CancellationTokenRegistration cancellationTokenRegistration,
    Memory<byte> packetBuffer,
    Memory<DivertAddress> addresses
) : IDisposable
{
    private MemoryHandle packetBufferHandle = packetBuffer.Pin();
    private MemoryHandle addressesHandle = addresses.Pin();

    public readonly NativeOverlapped* NativeOverlapped => nativeOverlapped;
    public readonly MemoryHandle PacketBufferHandle => packetBufferHandle;
    public readonly MemoryHandle AddressesHandle => addressesHandle;

    public readonly Memory<byte> PacketBuffer => packetBuffer;
    public readonly Memory<DivertAddress> Addresses => addresses;
    public readonly CancellationToken CancellationToken => cancellationTokenRegistration.Token;

    public void Dispose()
    {
        cancellationTokenRegistration.Dispose();
        packetBufferHandle.Dispose();
        addressesHandle.Dispose();
    }
}
