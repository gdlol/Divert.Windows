using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Runtime.Versioning;
using Divert.Windows;

// Directs all HTTP traffic to a localhost:8080.

[assembly: SupportedOSPlatform("windows6.0.6000")]

if (!(args is [string arg, ..] && ushort.TryParse(arg, out ushort listenPort)))
{
    listenPort = 8080;
}

Console.WriteLine($"Redirecting all HTTP traffic to http://localhost:{listenPort}...");
using var listener = new HttpListener() { Prefixes = { $"http://*:{listenPort}/" } };
listener.Start();
var listen = Task.Run(async () =>
{
    var buffer = "Hello from local HTTP server!"u8.ToArray();
    while (true)
    {
        var context = await listener.GetContextAsync();
        _ = Task.Run(async () =>
        {
            var remoteEndPoint = context.Request.RemoteEndPoint;
            Console.WriteLine(
                $"Received request from {remoteEndPoint.Address}:{remoteEndPoint.Port} for {context.Request.Url}"
            );
            var response = context.Response;
            response.ContentLength64 = buffer.Length;
            response.ContentType = "text/plain";
            await response.OutputStream.WriteAsync(buffer);
            response.OutputStream.Close();
            response.Close();
        });
    }
});

using var outService = new DivertService(DivertFilter.Outbound & DivertFilter.RemotePort == 80);
using var inService = new DivertService(
    (DivertFilter.RemoteAddress == IPAddress.Loopback | DivertFilter.RemoteAddress == IPAddress.IPv6Loopback)
        & DivertFilter.LocalPort == listenPort
);
var portMapping = new ConcurrentDictionary<ushort, (IPAddress source, IPAddress destination)>();

var redirect = Task.Run(async () =>
{
    var buffer = new byte[ushort.MaxValue + 40];
    var addresses = new DivertAddress[1];
    while (true)
    {
        var result = await outService.ReceiveAsync(buffer, addresses);
        ushort sourcePort;
        IPAddress originalSource;
        IPAddress originalDestination;
        if (addresses[0].IsIPv6)
        {
            sourcePort = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(40));
            originalSource = new IPAddress(buffer.AsSpan(8, 16));
            originalDestination = new IPAddress(buffer.AsSpan(24, 16));
            IPAddress.IPv6Loopback.GetAddressBytes().CopyTo(buffer, 8);
            IPAddress.IPv6Loopback.GetAddressBytes().CopyTo(buffer, 24);
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(42), listenPort);
        }
        else
        {
            int ihl = (buffer[0] & 0x0F) * 4;
            sourcePort = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(ihl));
            originalSource = new IPAddress(buffer.AsSpan(12, 4));
            originalDestination = new IPAddress(buffer.AsSpan(16, 4));
            IPAddress.Loopback.GetAddressBytes().CopyTo(buffer, 12);
            IPAddress.Loopback.GetAddressBytes().CopyTo(buffer, 16);
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(ihl + 2), listenPort);
        }
        Console.WriteLine(
            $"Redirecting outbound HTTP packet from {originalSource}:{sourcePort} to {originalDestination}:80..."
        );
        portMapping[sourcePort] = (originalSource, originalDestination);
        var packet = buffer.AsMemory(0, result.DataLength);
        DivertHelper.CalculateChecksums(packet.Span, ref addresses[0]);
        await outService.SendAsync(packet, addresses);
    }
});

var inject = Task.Run(async () =>
{
    var buffer = new byte[ushort.MaxValue + 40];
    var addresses = new DivertAddress[1];
    while (true)
    {
        (int packetLength, _) = await inService.ReceiveAsync(buffer, addresses);
        ushort targetPort;
        IPAddress? originalSource = null;
        IPAddress? originalDestination = null;
        if (addresses[0].IsIPv6)
        {
            targetPort = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(42));
            if (portMapping.TryGetValue(targetPort, out var value))
            {
                (originalSource, originalDestination) = value;
                originalDestination.GetAddressBytes().CopyTo(buffer, 8);
                originalSource.GetAddressBytes().CopyTo(buffer, 24);
                BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(40), 80);
            }
        }
        else
        {
            int ihl = (buffer[0] & 0x0F) * 4;
            targetPort = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(ihl + 2));
            if (portMapping.TryGetValue(targetPort, out var value))
            {
                (originalSource, originalDestination) = value;
                originalDestination.GetAddressBytes().CopyTo(buffer, 12);
                originalSource.GetAddressBytes().CopyTo(buffer, 16);
                BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(ihl), 80);
            }
        }
        if (originalSource is not null)
        {
            Console.WriteLine(
                $"Injecting inbound HTTP packet from {originalDestination}:{80} to {originalSource}:{targetPort}..."
            );
        }
        var packet = buffer.AsMemory(0, packetLength);
        DivertHelper.CalculateChecksums(packet.Span, ref addresses[0]);
        await inService.SendAsync(packet, addresses);
    }
});

using var client = new HttpClient();
string response = await client.GetStringAsync("http://example.com/");
Console.WriteLine($"Response from example.com (injected): {response}");

Console.WriteLine("Ready.");

await await Task.WhenAny(listen, redirect, inject);
