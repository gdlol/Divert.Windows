using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Divert.Windows.Tests;

[TestClass]
public class ChecksumTests : DivertTests
{
    [TestMethod]
    public async Task InvalidChecksum()
    {
        using var listener = CreateUdpListener(out int port);
        var receive = listener.ReceiveAsync(Token).AsTask();

        var filter =
            DivertFilter.UDP
            & DivertFilter.Loopback
            & DivertFilter.Ip
            & !DivertFilter.Impostor
            & (DivertFilter.RemotePort == port);
        using var service = new DivertService(filter);

        var packetBuffer = new byte[ushort.MaxValue + 40];
        var addressBuffer = new DivertAddress[1];

        var divertReceive = service.ReceiveAsync(packetBuffer, addressBuffer, Token).AsTask();
        Assert.IsFalse(divertReceive.IsCompleted);

        // send 3 bytes payload
        using var client = new UdpClient();
        client.Connect(IPAddress.Loopback, port);
        await client.SendAsync(new byte[] { 1, 2, 3 }, Token);

        (int length, _) = await divertReceive;
        var packet = packetBuffer.AsMemory(0, length);

        // Calculate and then invalidate checksums
        Assert.IsTrue(DivertHelper.CalculateChecksums(packet.Span));
        ushort ipChecksum = BinaryPrimitives.ReadUInt16BigEndian(packet.Span[10..12]);
        ushort udpChecksum = BinaryPrimitives.ReadUInt16BigEndian(packet.Span[26..28]);
        BinaryPrimitives.WriteUInt16BigEndian(packet.Span[10..12], (ushort)~ipChecksum); // invalidate IP checksum
        BinaryPrimitives.WriteUInt16BigEndian(packet.Span[26..28], (ushort)~udpChecksum); // invalidate UDP checksum

        // Recalculate
        addressBuffer[0].IsIPChecksumValid = false;
        addressBuffer[0].IsTCPChecksumValid = false;
        addressBuffer[0].IsUDPChecksumValid = false;
        Assert.IsTrue(DivertHelper.CalculateChecksums(packet.Span, ref addressBuffer[0]));
        Assert.AreEqual(ipChecksum, BinaryPrimitives.ReadUInt16BigEndian(packet.Span[10..12]));
        Assert.AreEqual(udpChecksum, BinaryPrimitives.ReadUInt16BigEndian(packet.Span[26..28]));
        Assert.IsTrue(addressBuffer[0].IsIPChecksumValid);
        Assert.IsTrue(addressBuffer[0].IsUDPChecksumValid);
        Assert.IsFalse(addressBuffer[0].IsTCPChecksumValid);

        // Recalculate only UDP checksum
        BinaryPrimitives.WriteUInt16BigEndian(packet.Span[10..12], (ushort)~ipChecksum); // invalidate IP checksum
        BinaryPrimitives.WriteUInt16BigEndian(packet.Span[26..28], (ushort)~udpChecksum); // invalidate UDP checksum
        addressBuffer[0].IsIPChecksumValid = false;
        addressBuffer[0].IsTCPChecksumValid = false;
        addressBuffer[0].IsUDPChecksumValid = false;
        Assert.IsTrue(
            DivertHelper.CalculateChecksums(packet.Span, ref addressBuffer[0], DivertHelperFlags.NoIPChecksum)
        );
        Assert.AreNotEqual(ipChecksum, BinaryPrimitives.ReadUInt16BigEndian(packet.Span[10..12]));
        Assert.AreEqual(udpChecksum, BinaryPrimitives.ReadUInt16BigEndian(packet.Span[26..28]));
        Assert.IsFalse(addressBuffer[0].IsIPChecksumValid);
        Assert.IsTrue(addressBuffer[0].IsUDPChecksumValid);
        Assert.IsFalse(addressBuffer[0].IsTCPChecksumValid);

        // Recalculate only IP checksum
        BinaryPrimitives.WriteUInt16BigEndian(packet.Span[10..12], (ushort)~ipChecksum); // invalidate IP checksum
        BinaryPrimitives.WriteUInt16BigEndian(packet.Span[26..28], (ushort)~udpChecksum); // invalidate UDP checksum
        Assert.IsTrue(DivertHelper.CalculateChecksums(packet.Span, DivertHelperFlags.NoUDPChecksum));
        Assert.AreEqual(ipChecksum, BinaryPrimitives.ReadUInt16BigEndian(packet.Span[10..12]));
        Assert.AreNotEqual(udpChecksum, BinaryPrimitives.ReadUInt16BigEndian(packet.Span[26..28]));

        // Invalid packet
        Assert.IsFalse(DivertHelper.CalculateChecksums(default));
        Assert.AreEqual(0, Marshal.GetLastPInvokeError());
        Assert.IsFalse(DivertHelper.CalculateChecksums(default, ref addressBuffer[0]));
        Assert.AreEqual(0, Marshal.GetLastPInvokeError());
    }
}
