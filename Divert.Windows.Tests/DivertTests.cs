using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Divert.Windows.Tests;

public abstract class DivertTests : IDisposable
{
    private readonly CancellationTokenSource cts;
    private readonly CancellationToken token;

    protected CancellationToken Token => token;

    public DivertTests()
    {
        cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        token = cts.Token;
    }

    public void Dispose()
    {
        cts.Dispose();
        GC.SuppressFinalize(this);
    }

    public static UdpClient CreateUdpListener(out int port)
    {
        var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var localEndPoint = (IPEndPoint)client.Client.LocalEndPoint!;
        port = localEndPoint.Port;
        return client;
    }

    public static int GetLoopbackInterfaceIndex()
    {
        return NetworkInterface
            .GetAllNetworkInterfaces()
            .Single(i => i.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            .GetIPProperties()
            .GetIPv4Properties()
            .Index;
    }
}
