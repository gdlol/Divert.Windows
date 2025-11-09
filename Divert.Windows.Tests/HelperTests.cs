using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using Windows.Win32.Foundation;

namespace Divert.Windows.Tests;

[TestClass]
public class HelperTests : DivertTests
{
    [TestMethod]
    public void FormatFilter()
    {
        var invalidFilter = Array.Empty<byte>();
        var exception = Assert.Throws<Win32Exception>(() =>
            DivertHelper.FormatFilter(invalidFilter, DivertLayer.Network)
        );
        Assert.AreEqual((int)WIN32_ERROR.ERROR_INVALID_PARAMETER, exception.NativeErrorCode);
    }

    [TestMethod]
    public async Task DecrementTtl()
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

        await client.SendAsync(new byte[] { 1, 2, 3 }, Token);
        var divertResult = await divertReceive;
        var packet = packetBuffer.AsMemory(0, divertResult.DataLength);

        // set ttl to 2
        packet.Span[8] = 2;
        Assert.IsTrue(DivertHelper.DecrementTtl(packet.Span));
        Assert.AreEqual(1, packet.Span[8]);
        Assert.IsFalse(DivertHelper.DecrementTtl(packet.Span));
        Assert.AreEqual(1, packet.Span[8]);
    }

    [TestMethod]
    public async Task CompileFilter()
    {
        using var listener = CreateUdpListener(out int port);
        var receive = listener.ReceiveAsync(Token).AsTask();

        var filter =
            DivertFilter.UDP
            & DivertFilter.Loopback
            & DivertFilter.Ip
            & !DivertFilter.Impostor
            & (DivertFilter.RemotePort == port);
        var filterBuffer = DivertHelper.CompileFilter(filter, DivertLayer.Network);
        using var service = new DivertService(filterBuffer);

        var packetBuffer = new byte[ushort.MaxValue + 40];
        var addressBuffer = new DivertAddress[1];

        var divertReceive = service.ReceiveAsync(packetBuffer, addressBuffer, Token).AsTask();
        Assert.IsFalse(divertReceive.IsCompleted);

        // send 3 bytes payload
        using var client = new UdpClient();
        client.Connect(IPAddress.Loopback, port);

        await client.SendAsync(new byte[] { 1, 2, 3 }, Token);
        var divertResult = await divertReceive;

        Assert.AreEqual(20 + 8 + 3, divertResult.DataLength);
        var packet = packetBuffer.AsMemory(0, divertResult.DataLength);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, packet.ToArray()[28..]);
    }

    private static IEnumerable<object[]> InvalidFilterCases() => DivertServiceTests.InvalidFilterCases();

    [TestMethod]
    [DynamicData(nameof(InvalidFilterCases))]
    public void CompileInvalidFilter(DivertLayer layer, DivertFilter filter, string message)
    {
        var exception = Assert.Throws<ArgumentException>(() => DivertHelper.CompileFilter(filter, layer));
        Assert.AreEqual("filter", exception.ParamName);
        Assert.StartsWith(message, exception.Message);
    }

    [TestMethod]
    public async Task EvaluateFilter()
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

        await client.SendAsync(new byte[] { 1, 2, 3 }, Token);
        var divertResult = await divertReceive;
        var packet = packetBuffer.AsMemory(0, divertResult.DataLength);
        var address = addressBuffer[0];

        Assert.IsTrue(DivertHelper.EvaluateFilter(filter, packet.Span, address));
        Assert.IsTrue(
            DivertHelper.EvaluateFilter(DivertHelper.CompileFilter(filter, DivertLayer.Network), packet.Span, address)
        );
        Assert.IsTrue(DivertHelper.EvaluateFilter(DivertFilter.UDP, packet.Span, address));
        Assert.IsFalse(DivertHelper.EvaluateFilter(DivertFilter.TCP, packet.Span, address));

        var exception = Assert.Throws<Win32Exception>(() => DivertHelper.EvaluateFilter([], packet.Span, address));
        Assert.AreEqual((int)WIN32_ERROR.ERROR_INVALID_PARAMETER, exception.NativeErrorCode);
        exception = Assert.Throws<Win32Exception>(() => DivertHelper.EvaluateFilter(filter, [], address));
        Assert.AreEqual((int)WIN32_ERROR.ERROR_INVALID_PARAMETER, exception.NativeErrorCode);
    }
}
