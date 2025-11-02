using System.Net;
using System.Net.Sockets;

namespace Divert.Windows.Tests;

[TestClass]
public sealed class DivertServiceTests : IDisposable
{
    private readonly CancellationTokenSource cts;
    private readonly CancellationToken token;

    public DivertServiceTests()
    {
        cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        token = cts.Token;
    }

    public void Dispose()
    {
        cts.Dispose();
    }

    private static UdpClient CreateUdpListener(out int port)
    {
        var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var localEndPoint = (IPEndPoint)client.Client.LocalEndPoint!;
        port = localEndPoint.Port;
        return client;
    }

    [TestMethod]
    public async Task ModifyPayload()
    {
        using var listener = CreateUdpListener(out int port);
        var receive = listener.ReceiveAsync(token).AsTask();

        var filter =
            DivertFilter.UDP
            & DivertFilter.Loopback
            & DivertFilter.Ip
            & !DivertFilter.Impostor
            & (DivertFilter.RemotePort == port.ToString());
        using var service = new DivertService(filter);

        var packetBuffer = new byte[ushort.MaxValue + 40];
        var addressBuffer = new DivertAddress[1];

        var divertReceive = service.ReceiveAsync(packetBuffer, addressBuffer, token).AsTask();
        Assert.IsFalse(divertReceive.IsCompleted);

        // send 3 bytes payload
        using var client = new UdpClient();
        client.Connect(IPAddress.Loopback, port);
        await client.SendAsync(new byte[] { 1, 2, 3 }, token);

        var divertResult = await divertReceive;
        Assert.AreEqual(20 + 8 + 3, divertResult.Length);
        var packet = packetBuffer.AsMemory(0, divertResult.Length);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, packet.ToArray()[28..]);
        Assert.IsFalse(receive.IsCompleted);

        // Re-inject
        addressBuffer[0] = new DivertAddress { IsImpostor = true, IsOutbound = true };
        await service.SendAsync(packet, addressBuffer, token);
        var result = await receive;
        Assert.HasCount(3, result.Buffer);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, result.Buffer);

        // Re-inject with modified data
        receive = listener.ReceiveAsync(token).AsTask();
        new byte[] { 4, 5, 6 }.CopyTo(packet.Span[28..]);
        DivertHelper.CalculateChecksums(packet.Span);
        Assert.IsFalse(receive.IsCompleted);
        await service.SendAsync(packet, addressBuffer, token);
        result = await receive;
        Assert.HasCount(3, result.Buffer);
        CollectionAssert.AreEqual(new byte[] { 4, 5, 6 }, result.Buffer);
    }
}
