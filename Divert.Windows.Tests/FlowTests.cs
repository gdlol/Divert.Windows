using System.Net;
using System.Net.Sockets;

namespace Divert.Windows.Tests;

[TestClass]
public class FlowTests : DivertTests
{
    [TestMethod]
    public async Task FlowData()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var filter = DivertFilter.TCP & DivertFilter.Loopback & (DivertFilter.RemotePort == port);
        using var service = new DivertService(
            filter,
            DivertLayer.Flow,
            flags: DivertFlags.Sniff | DivertFlags.ReceiveOnly
        );

        var addressBuffer = new DivertAddress[1];
        var divertReceive = service.ReceiveAsync(default, addressBuffer, Token).AsTask();
        Assert.IsFalse(divertReceive.IsCompleted);

        using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var accept = listener.AcceptSocketAsync(Token);
        Assert.IsFalse(accept.IsCompleted);
        await client.ConnectAsync(IPAddress.Loopback, port, Token);
        int clientPort = ((IPEndPoint)client.LocalEndPoint!).Port;
        using var serverSocket = await accept;
        await client.SendAsync(new byte[] { 1, 2, 3 }, SocketFlags.None);

        var divertResult = await divertReceive;
        var address = addressBuffer[0];
        Assert.AreEqual(0, divertResult.DataLength);
        Assert.AreEqual(DivertLayer.Flow, address.Layer);
        Assert.AreEqual(DivertEvent.FlowEstablished, address.Event);
        Assert.Throws<InvalidOperationException>(() => address.GetNetworkData());
        var flowData = address.GetFlowData();
        Assert.AreEqual(Environment.ProcessId, (int)flowData.ProcessId);
        Assert.AreEqual(IPAddress.Loopback, flowData.LocalAddress);
        Assert.AreEqual(IPAddress.Loopback, flowData.RemoteAddress);
        Assert.AreEqual((ushort)port, flowData.RemotePort);
        Assert.AreEqual((ushort)clientPort, flowData.LocalPort);
        Assert.AreEqual((byte)ProtocolType.Tcp, flowData.Protocol);

        addressBuffer[0].Reset();
        divertReceive = service.ReceiveAsync(default, addressBuffer, Token).AsTask();

        client.Shutdown(SocketShutdown.Both);
        serverSocket.Shutdown(SocketShutdown.Both);
        serverSocket.Close();
        client.Close();

        divertResult = await divertReceive;
        address = addressBuffer[0];
        Assert.AreEqual(DivertLayer.Flow, address.Layer);
        Assert.AreEqual(DivertEvent.FlowDeleted, address.Event);
        flowData = address.GetFlowData();
        Assert.AreEqual(Environment.ProcessId, (int)flowData.ProcessId);
        Assert.AreEqual(IPAddress.Loopback, flowData.LocalAddress);
        Assert.AreEqual(IPAddress.Loopback, flowData.RemoteAddress);
        Assert.AreEqual((ushort)port, flowData.RemotePort);
        Assert.AreEqual((ushort)clientPort, flowData.LocalPort);
        Assert.AreEqual((byte)ProtocolType.Tcp, flowData.Protocol);
    }
}
