using System.Net;
using System.Net.Sockets;

namespace Divert.Windows.Tests;

[TestClass]
public class SocketTests : DivertTests
{
    [TestMethod]
    public async Task SocketData()
    {
        var filter = DivertFilter.TCP & DivertFilter.Loopback & (DivertFilter.ProcessId == Environment.ProcessId);
        using var service = new DivertService(
            filter,
            DivertLayer.Socket,
            flags: DivertFlags.Sniff | DivertFlags.ReceiveOnly
        );

        var addressBuffer = new DivertAddress[1];
        var divertReceive = service.ReceiveAsync(default, addressBuffer, Token).AsTask();
        Assert.IsFalse(divertReceive.IsCompleted);

        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)socket.LocalEndPoint!).Port;

        var divertResult = await divertReceive;
        var address = addressBuffer[0];
        Assert.AreEqual(0, divertResult.DataLength);
        Assert.AreEqual(DivertLayer.Socket, address.Layer);
        Assert.AreEqual(DivertEvent.SocketBind, address.Event);
        var socketData = address.GetSocketData();
        Assert.AreEqual(Environment.ProcessId, (int)socketData.ProcessId);
        Assert.AreEqual(IPAddress.Loopback, socketData.LocalAddress);
        Assert.AreEqual(IPAddress.Any, socketData.RemoteAddress);
        Assert.AreEqual((ushort)port, socketData.LocalPort);
        Assert.AreEqual(0, socketData.RemotePort);
        Assert.AreEqual((byte)ProtocolType.Tcp, socketData.Protocol);
    }
}
