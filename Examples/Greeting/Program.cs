using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text;
using Divert.Windows;

// Captures a UDP packet and print to console.

[assembly: SupportedOSPlatform("windows6.0.6000")]

using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
var localEndPoint = (IPEndPoint)client.Client.LocalEndPoint!;
int port = localEndPoint.Port;
Console.WriteLine($"Created UDP client on port {port}.");

using var service = new DivertService(DivertFilter.UDP & DivertFilter.LocalPort == port);
var buffer = new byte[1024];
var receive = service.ReceiveAsync(buffer, new DivertAddress[1]).AsTask();

Console.WriteLine("Sending packet to self...");
await client.SendAsync("Hello"u8.ToArray(), localEndPoint);

(int packetLength, _) = await receive;
string message = Encoding.UTF8.GetString(buffer.AsSpan(28, packetLength));
Console.WriteLine($"{message} from WinDivert!");
