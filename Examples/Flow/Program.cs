using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using Divert.Windows;

[assembly: SupportedOSPlatform("windows6.0.6000")]

// Logs flow events on a loopback TCP listener.

using var service = new DivertService(
    DivertFilter.ProcessId == Environment.ProcessId & DivertFilter.TCP,
    DivertLayer.Flow,
    flags: DivertFlags.Sniff | DivertFlags.ReceiveOnly
)
{
    QueueTime = DivertService.MaxQueueTime,
};

static void WriteLine(string prefix, string message)
{
    Console.WriteLine($"{prefix}: {message}");
}

using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
listener.Listen();
int port = ((IPEndPoint)listener.LocalEndPoint!).Port;
WriteLine(nameof(TcpListener), $"Listening on port {port}...");
using var cts = new CancellationTokenSource();
var token = cts.Token;

var sniff = Task.Run(async () =>
{
    try
    {
        var addresses = new DivertAddress[1];
        while (true)
        {
            await service.ReceiveAsync(Memory<byte>.Empty, addresses, token);
            var @event = addresses[0].Event;
            var socketData = addresses[0].GetFlowData();
            if (socketData.LocalPort == port)
            {
                WriteLine(
                    nameof(DivertService),
                    $"{@event} from {nameof(TcpListener)}, "
                        + $"local port = {socketData.LocalPort}, remote port = {socketData.RemotePort}."
                );
            }
            else
            {
                WriteLine(
                    nameof(DivertService),
                    $"{@event} from {nameof(TcpClient)}, "
                        + $"local port = {socketData.LocalPort}, remote port = {socketData.RemotePort}."
                );
            }
        }
    }
    catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    catch (Exception e)
    {
        WriteLine("Error", e.ToString());
    }
});

var listen = Task.Run(async () =>
{
    try
    {
        while (true)
        {
            var client = await listener.AcceptAsync(token);
            int clientPort = ((IPEndPoint)client.RemoteEndPoint!).Port;
            WriteLine(nameof(TcpListener), $"Accepted connection from port {clientPort}.");
            _ = Task.Run(async () =>
            {
                using var _ = client;
                var buffer = new byte[1024];

                while (true)
                {
                    int length = await client.ReceiveAsync(buffer, token);
                    if (length is 0)
                    {
                        WriteLine(nameof(TcpListener), "Closing connection...");
                        client.Shutdown(SocketShutdown.Send);
                        break;
                    }
                }
            });
        }
    }
    catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    catch (Exception e)
    {
        WriteLine("Error", e.ToString());
    }
});

WriteLine("Info", "Preparing TCP client...");
try
{
    for (int i = 0; i < 3; i++)
    {
        await Task.Delay(TimeSpan.FromSeconds(3), token);
        Console.WriteLine();
        using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        WriteLine(nameof(TcpClient), $"Connecting to port {port}...");
        await client.ConnectAsync(IPAddress.Loopback, port, token);
        WriteLine(nameof(TcpClient), $"Connected to port {port}.");

        WriteLine(nameof(TcpClient), "Closing connection...");
        client.Shutdown(SocketShutdown.Send);
        var buffer = new byte[1024];
        while (true)
        {
            int length = await client.ReceiveAsync(buffer, token);
            if (length is 0)
            {
                break;
            }
        }
    }
}
catch (Exception e)
{
    WriteLine("Error", e.ToString());
}

await Task.Delay(TimeSpan.FromSeconds(3), token);
cts.Cancel();
await Task.WhenAll(sniff, listen).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
Console.WriteLine();
WriteLine("Info", "Done.");
Console.ReadLine();
