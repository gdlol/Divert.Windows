using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using System.Security.Principal;
using Divert.Windows;

[assembly: SupportedOSPlatform("windows6.0.6000")]

var identity = WindowsIdentity.GetCurrent();
var principal = new WindowsPrincipal(identity);
if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
{
    var startInfo = new ProcessStartInfo
    {
        FileName = Environment.ProcessPath,
        Verb = "runas",
        Arguments = args.Length > 0 ? args[0] : string.Empty,
        UseShellExecute = true
    };
    Process.Start(startInfo);
    Environment.Exit(0);
}

string remoteIP = args.Length > 0 ? args[0] : "8.8.8.8";

var outboundFilter =
    DivertFilter.Outbound
    & !DivertFilter.Loopback
    & DivertFilter.RemoteAddress == remoteIP
    & DivertFilter.ICMP;
var inboundFilter =
    DivertFilter.Inbound
    & DivertFilter.RemoteAddress == remoteIP
    & DivertFilter.ICMP;
Console.WriteLine($"{nameof(outboundFilter)}: {outboundFilter}");
Console.WriteLine($"{nameof(inboundFilter)}: {inboundFilter}");

using var outDivert = new DivertService(outboundFilter);
using var inDivert = new DivertService(inboundFilter);

var ping = Task.Run(async () =>
{
    using var ping = new Ping();
    while (true)
    {
        await Task.Delay(TimeSpan.FromSeconds(1));
        var buffer = BitConverter.GetBytes(DateTimeOffset.Now.ToUnixTimeMilliseconds());
        Console.WriteLine($"Pinging {remoteIP} with {buffer.Length} bytes of data (Ping):");
        var reply = await ping.SendPingAsync(remoteIP, 3000, buffer);
        if (reply.Status == IPStatus.Success)
        {
            Console.WriteLine(
                $"Reply from {reply.Address} (Ping): "
                + $"bytes={reply.Buffer?.Length} "
                + $"time={reply.RoundtripTime}ms "
                + $"TTL={reply.Options?.Ttl}");
        }
        else
        {
            Console.WriteLine(reply.Status);
        }
    }
});

var outbound = Task.Run(() =>
{
    using var cts = new CancellationTokenSource();
    cts.CancelAfter(3500);
    var buffer = new byte[ushort.MaxValue];
    var addresses = new DivertAddress[1];
    while (true)
    {
        try
        {
            var (packetLength, _) = outDivert.ReceiveEx(buffer, addresses, cts.Token);
            var packet = buffer.AsSpan(0, packetLength);
            var remoteAddress = new IPAddress(packet[16..20]);
            Console.WriteLine($"Pinging {remoteAddress} with {packet.Length - 28} bytes of data (Divert):");
            outDivert.Send(packet, addresses[0]);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Outbound Cancelled.");
            break;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            throw;
        }
    }
});

var inbound = Task.Run(() =>
{
    using var cts = new CancellationTokenSource();
    cts.CancelAfter(2500);
    var buffer = new byte[ushort.MaxValue];
    while (true)
    {
        try
        {
            var (packetLength, address) = inDivert.Receive(buffer);
            var packet = buffer.AsSpan(0, packetLength);
            var remoteAddress = new IPAddress(packet[12..16]);
            long timestamp = BitConverter.ToInt64(packet.Slice(28, sizeof(long)));
            Console.WriteLine(
                    $"Reply from {remoteAddress} (Divert): "
                    + $"bytes={packet.Length - 28} "
                    + $"time={DateTimeOffset.Now.ToUnixTimeMilliseconds() - timestamp}ms "
                    + $"TTL={packet[8]}");
            inDivert.SendEx(packet, new[] { address }, cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Inbound Cancelled.");
            break;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            throw;
        }
    }
});

await Task.WhenAll(ping, outbound, inbound);
