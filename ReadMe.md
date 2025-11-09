# Divert.Windows

[CI Badge]: https://img.shields.io/github/actions/workflow/status/gdlol/Divert.Windows/.github%2Fworkflows%2Fmain.yml
[CI URL]: https://github.com/gdlol/Divert.Windows/actions/workflows/main.yml
[Codecov Badge]: https://img.shields.io/codecov/c/github/gdlol/Divert.Windows
[Codecov URL]: https://codecov.io/gh/gdlol/Divert.Windows
[License Badge]: https://img.shields.io/github/license/gdlol/Divert.Windows

[![CI Badge][CI Badge]][CI URL]
[![Codecov Badge][Codecov Badge]][Codecov URL]
[![License Badge][License Badge]](LICENSE)

High quality .NET APIs for WinDivert.

See https://reqrypt.org/windivert.html.

# Example

```csharp
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
```

See also [Examples/](Examples/)

# License

[LGPL-3.0](LICENSE)
