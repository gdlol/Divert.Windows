using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;

namespace Divert.Windows.Tests;

[TestClass]
public sealed class DivertServiceTests : DivertTests
{
    private static IEnumerable<object[]> InvalidFilterCases()
    {
        return
        [
            [
                DivertLayer.Network,
                DivertFilter.Protocol == "invalid",
                "Filter expression contains a bad token (11): ...invalid",
            ],
            [
                DivertLayer.Network,
                DivertFilter.Ip & DivertFilter.Layer == DivertLayer.Network & DivertFilter.Loopback,
                "Filter expression contains a bad token for layer (7): ...layer = NETWORK and loopback",
            ],
            [
                DivertLayer.Network,
                DivertFilter.Ip & DivertFilter.Event == DivertEvent.SocketBind & DivertFilter.Loopback,
                "Filter expression parse error (15): ...BIND and loopback",
            ],
        ];
    }

    [TestMethod]
    [DynamicData(nameof(InvalidFilterCases))]
    public void InvalidFilter(DivertLayer layer, DivertFilter filter, string message)
    {
        var exception = Assert.Throws<ArgumentException>(() => new DivertService(filter, layer: layer));
        Assert.AreEqual("filter", exception.ParamName);
        Assert.StartsWith(message, exception.Message);
    }

    [TestMethod]
    public async Task ModifyPayload()
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

        long begin = Stopwatch.GetTimestamp();
        await client.SendAsync(new byte[] { 1, 2, 3 }, Token);

        var divertResult = await divertReceive;
        long end = Stopwatch.GetTimestamp();

        Assert.AreEqual(20 + 8 + 3, divertResult.DataLength);
        var packet = packetBuffer.AsMemory(0, divertResult.DataLength);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, packet.ToArray()[28..]);
        Assert.AreEqual(1, divertResult.AddressLength);
        Assert.IsFalse(receive.IsCompleted);
        Assert.AreEqual(DivertLayer.Network, addressBuffer[0].Layer);
        Assert.IsGreaterThanOrEqualTo(begin, addressBuffer[0].Timestamp);
        Assert.IsLessThanOrEqualTo(end, addressBuffer[0].Timestamp);
        Assert.AreEqual(DivertEvent.NetworkPacket, addressBuffer[0].Event);
        Assert.IsFalse(addressBuffer[0].IsSniffed);
        Assert.IsTrue(addressBuffer[0].IsOutbound);
        Assert.IsTrue(addressBuffer[0].IsLoopback);
        Assert.IsFalse(addressBuffer[0].IsImpostor);
        Assert.IsFalse(addressBuffer[0].IsIPv6);
        var networkData = addressBuffer[0].GetNetworkData();
        Assert.AreEqual(GetLoopbackInterfaceIndex(), (int)networkData.InterfaceIndex);
        Assert.AreEqual(0, (int)networkData.SubInterfaceIndex);
        Assert.Throws<InvalidOperationException>(() => addressBuffer[0].GetFlowData());
        Assert.Throws<InvalidOperationException>(() => addressBuffer[0].GetSocketData());
        Assert.Throws<InvalidOperationException>(() => addressBuffer[0].GetReflectData());

        // Re-inject
        addressBuffer[0] = new DivertAddress(1, 1) { IsImpostor = true, IsOutbound = true };
        await service.SendAsync(packet, addressBuffer, Token);
        var result = await receive;
        Assert.HasCount(3, result.Buffer);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, result.Buffer);

        // Re-inject with modified data
        addressBuffer[0].Reset();
        addressBuffer[0].IsImpostor = true;
        addressBuffer[0].IsOutbound = true;
        receive = listener.ReceiveAsync(Token).AsTask();
        new byte[] { 4, 5, 6 }.CopyTo(packet.Span[28..]);
        Assert.IsTrue(DivertHelper.CalculateChecksums(packet.Span));
        Assert.IsFalse(receive.IsCompleted);
        await service.SendAsync(packet, addressBuffer, CancellationToken.None);
        result = await receive;
        Assert.HasCount(3, result.Buffer);
        CollectionAssert.AreEqual(new byte[] { 4, 5, 6 }, result.Buffer);
    }

    [TestMethod]
    public async Task Close()
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

        service.Dispose();
        var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () => await divertReceive);
        Assert.IsFalse(Token.IsCancellationRequested);
        Assert.AreNotEqual(Token, exception.CancellationToken);
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
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(Token);
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
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(Token);
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

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var token = cts.Token;
        cts.Cancel();
        var divertSend = service.SendAsync(packetBuffer, addressBuffer, token).AsTask();
        Assert.IsTrue(divertSend.IsCanceled);
    }

    [TestMethod]
    public async Task InsufficientBuffer()
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

        var packetBuffer = new byte[10]; // insufficient buffer
        var addressBuffer = new DivertAddress[1];

        var divertReceive = service.ReceiveAsync(packetBuffer, addressBuffer, Token).AsTask();
        Assert.IsFalse(divertReceive.IsCompleted);

        // send 3 bytes payload
        using var client = new UdpClient();
        client.Connect(IPAddress.Loopback, port);
        await client.SendAsync(new byte[] { 1, 2, 3 }, Token);

        var exception = await Assert.ThrowsAsync<Win32Exception>(async () => await divertReceive);
        Assert.AreEqual((int)WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER, exception.NativeErrorCode);
    }

    [TestMethod]
    public async Task DisposeOnReceive()
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

        using var pipe = new ExecutorDelayPipe();
        var divertReceive = Task.Run(async () => await service.ReceiveAsync(packetBuffer, addressBuffer, Token));
        Assert.IsFalse(divertReceive.IsCompleted);

        await pipe.Stream.WaitForConnectionAsync(Token);
        Assert.IsFalse(divertReceive.IsCompleted);
        service.Dispose();
        pipe.Stream.WriteByte(0);
        await pipe.Stream.FlushAsync(Token);
        pipe.Stream.Disconnect();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await divertReceive);
    }

    [TestMethod]
    public async Task InvalidHandle()
    {
        var handle = PInvoke.CreateFile(
            Path.GetTempFileName(),
            (uint)GENERIC_ACCESS_RIGHTS.GENERIC_READ,
            FILE_SHARE_MODE.FILE_SHARE_NONE,
            null,
            FILE_CREATION_DISPOSITION.OPEN_EXISTING,
            FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_NORMAL | FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_OVERLAPPED,
            null
        );
        using var service = new DivertService(handle);

        var packetBuffer = new byte[ushort.MaxValue + 40];
        var addressBuffer = new DivertAddress[1];
        var exception = await Assert.ThrowsAsync<Win32Exception>(async () =>
            await service.ReceiveAsync(packetBuffer, addressBuffer, Token)
        );
        Assert.AreEqual((int)WIN32_ERROR.ERROR_INVALID_PARAMETER, exception.NativeErrorCode);
    }
}
