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
            & (DivertFilter.RemotePort == port);
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
        Assert.AreEqual(20 + 8 + 3, divertResult.DataLength);
        var packet = packetBuffer.AsMemory(0, divertResult.DataLength);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, packet.ToArray()[28..]);
        Assert.AreEqual(1, divertResult.AddressLength);
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

    [TestMethod]
    public async Task Close()
    {
        using var listener = CreateUdpListener(out int port);
        var receive = listener.ReceiveAsync(token).AsTask();

        var filter =
            DivertFilter.UDP
            & DivertFilter.Loopback
            & DivertFilter.Ip
            & !DivertFilter.Impostor
            & (DivertFilter.RemotePort == port);
        using var service = new DivertService(filter);

        var packetBuffer = new byte[ushort.MaxValue + 40];
        var addressBuffer = new DivertAddress[1];
        var divertReceive = service.ReceiveAsync(packetBuffer, addressBuffer, token).AsTask();
        Assert.IsFalse(divertReceive.IsCompleted);

        service.Dispose();
        var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () => await divertReceive);
        Assert.IsFalse(token.IsCancellationRequested);
        Assert.AreNotEqual(token, exception.CancellationToken);
        Assert.AreEqual(CancellationToken.None, exception.CancellationToken);
    }

    [TestMethod]
    public async Task CancelReceive()
    {
        using var listener = CreateUdpListener(out int port);

        var filter =
            DivertFilter.UDP
            & DivertFilter.Loopback
            & DivertFilter.Ip
            & !DivertFilter.Impostor
            & (DivertFilter.RemotePort == port);
        using var service = new DivertService(filter);

        var packetBuffer = new byte[ushort.MaxValue + 40];
        var addressBuffer = new DivertAddress[1];

        // Cancel before receive
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var token = cts.Token;
            var divertReceive = service.ReceiveAsync(packetBuffer, addressBuffer, token).AsTask();
            Assert.IsTrue(divertReceive.IsCanceled);
        }

        // Cancel on receive
        using (var pipe = new ExecutorDelayPipe())
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(this.token);
            var token = cts.Token;

            var divertReceive = Task.Run(async () => await service.ReceiveAsync(packetBuffer, addressBuffer, token));
            Assert.IsFalse(divertReceive.IsCompleted);

            await pipe.Stream.WaitForConnectionAsync(token);
            cts.Cancel();
            Assert.IsFalse(divertReceive.IsCompleted);
            pipe.Stream.WriteByte(0);
            await pipe.Stream.FlushAsync(token);
            pipe.Stream.Disconnect();

            var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () => await divertReceive);
            Assert.IsTrue(token.IsCancellationRequested);
            Assert.AreEqual(token, exception.CancellationToken);
        }

        // Cancel pending receive
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(this.token);
            var token = cts.Token;

            var divertReceive = service.ReceiveAsync(packetBuffer, addressBuffer, token).AsTask();
            Assert.IsFalse(divertReceive.IsCompleted);

            cts.Cancel();
            var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () => await divertReceive);
            Assert.IsTrue(token.IsCancellationRequested);
            Assert.AreEqual(token, exception.CancellationToken);
        }
    }

    [TestMethod]
    public void CancelSend()
    {
        using var service = new DivertService(DivertFilter.False);

        var packetBuffer = new byte[100];
        var addressBuffer = new DivertAddress[1];

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(this.token);
        var token = cts.Token;
        cts.Cancel();
        var divertSend = service.SendAsync(packetBuffer, addressBuffer, token).AsTask();
        Assert.IsTrue(divertSend.IsCanceled);
    }
}
