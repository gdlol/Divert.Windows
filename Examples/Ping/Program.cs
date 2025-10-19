using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
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
        UseShellExecute = true,
    };
    Process.Start(startInfo);
    Environment.Exit(0);
}

string? remoteIP = args.Length > 0 ? args[0] : null;
if (remoteIP is null)
{
    var gateway = NetworkInterface
        .GetAllNetworkInterfaces()
        .Where(nic =>
            nic.OperationalStatus == OperationalStatus.Up && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback
        )
        .SelectMany(nic => nic.GetIPProperties()?.GatewayAddresses?.ToArray() ?? [])
        .Select(g => g.Address)
        .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork);
    remoteIP = gateway?.ToString();
}
remoteIP ??= "1.1.1.1";

var outboundFilter =
    DivertFilter.Outbound & !DivertFilter.Loopback & DivertFilter.RemoteAddress == remoteIP & DivertFilter.ICMP;
var inboundFilter = DivertFilter.Inbound & DivertFilter.RemoteAddress == remoteIP & DivertFilter.ICMP;
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
                    + $"TTL={reply.Options?.Ttl}"
            );
        }
        else
        {
            Console.WriteLine(reply.Status);
            break;
        }
    }
});

var outbound = Task.Run(async () =>
{
    using var cts = new CancellationTokenSource();
    cts.CancelAfter(3500);
    var buffer = new byte[ushort.MaxValue];
    var addresses = new DivertAddress[1];
    while (true)
    {
        try
        {
            var result = await outDivert.ReceiveAsync(buffer, addresses, cts.Token).ConfigureAwait(false);
            var packet = buffer.AsMemory(0, result.Length);
            var remoteAddress = new IPAddress(packet[16..20].Span);
            Console.WriteLine($"Pinging {remoteAddress} with {packet.Length - 28} bytes of data (Divert):");
            await outDivert.SendAsync(packet, addresses, cts.Token);
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

var inbound = Task.Run(async () =>
{
    using var cts = new CancellationTokenSource();
    cts.CancelAfter(2500);
    var buffer = new byte[ushort.MaxValue];
    var addresses = new DivertAddress[1];
    while (true)
    {
        try
        {
            var receiveResult = await inDivert.ReceiveAsync(buffer, addresses, cts.Token).ConfigureAwait(false);
            var packet = buffer.AsMemory(0, receiveResult.Length);
            var remoteAddress = new IPAddress(packet[12..16].Span);
            long timestamp = BitConverter.ToInt64(packet.Slice(28, sizeof(long)).Span);
            Console.WriteLine(
                $"Reply from {remoteAddress} (Divert): "
                    + $"bytes={packet.Length - 28} "
                    + $"time={DateTimeOffset.Now.ToUnixTimeMilliseconds() - timestamp}ms "
                    + $"TTL={packet.Span[8]}"
            );
            await inDivert.SendAsync(packet, addresses, cts.Token);
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

await Task.WhenAll(ping, outbound, inbound).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
Console.WriteLine("Done.");
Console.ReadLine();
