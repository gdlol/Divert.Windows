using System.ComponentModel;
using System.Diagnostics;
using System.Net.Sockets;
using Windows.Win32.Foundation;

namespace Divert.Windows.Tests;

[TestClass]
public class ReflectTests : DivertTests
{
    [TestMethod]
    public async Task ReflectData()
    {
        using var reflectService = new DivertService(
            true,
            DivertLayer.Reflect,
            flags: DivertFlags.ReceiveOnly | DivertFlags.Sniff | DivertFlags.NoInstall
        );

        var dataBuffer = new byte[ushort.MaxValue];
        var addressBuffer = new DivertAddress[1];
        var divertReceive = reflectService.ReceiveAsync(dataBuffer, addressBuffer, Token).AsTask();
        Assert.IsFalse(divertReceive.IsCompleted);

        long begin = Stopwatch.GetTimestamp();
        using var service = new DivertService(false, priority: 5, flags: DivertFlags.WriteOnly);
        long end = Stopwatch.GetTimestamp();

        var divertResult = await divertReceive;
        var address = addressBuffer[0];
        Assert.AreEqual(DivertLayer.Reflect, address.Layer);
        Assert.AreEqual(DivertEvent.ReflectOpen, address.Event);
        var reflectData = address.GetReflectData();
        Assert.IsTrue(reflectData.Timestamp >= begin && reflectData.Timestamp <= end);
        Assert.AreEqual(Environment.ProcessId, (int)reflectData.ProcessId);
        Assert.AreEqual(DivertLayer.Network, reflectData.Layer);
        Assert.AreEqual(DivertFlags.WriteOnly, reflectData.Flags);
        Assert.AreEqual(5, reflectData.Priority);
        var packet = dataBuffer.AsSpan(0, divertResult.DataLength);
        string filterString = DivertHelper.FormatFilter(packet, reflectData.Layer);
        Assert.AreEqual("false", filterString);
    }

    public static IEnumerable<object[]> FormatFilterCases()
    {
        return
        [
            [
                DivertLayer.Socket,
                DivertFilter.Protocol == ProtocolType.Tcp
                    | DivertFilter.Protocol == ProtocolType.Udp
                    | DivertFilter.Protocol == ProtocolType.Icmp
                    | DivertFilter.Protocol == ProtocolType.IcmpV6,
                $"protocol = {(int)ProtocolType.Tcp} or "
                    + $"protocol = {(int)ProtocolType.Udp} or "
                    + $"protocol = {(int)ProtocolType.Icmp} or "
                    + $"protocol = {(int)ProtocolType.IcmpV6}",
            ],
            [DivertLayer.Socket, DivertFilter.Protocol == ProtocolType.IP, $"protocol = {(int)ProtocolType.IP}"],
            [DivertLayer.Socket, DivertFilter.Ip == true, "ip"],
            [DivertLayer.Socket, DivertFilter.Ip == false, "not ip"],
            [DivertLayer.Socket, DivertFilter.Ip != true, "not ip"],
            [DivertLayer.Socket, DivertFilter.Ip != false, "ip"],
            [DivertLayer.Socket, DivertFilter.Protocol == ProtocolType.Unknown, "false"],
            [DivertLayer.Socket, DivertFilter.Protocol != ProtocolType.Unknown, "true"],
            [DivertLayer.Socket, DivertFilter.Protocol == Enum.GetValues<ProtocolType>().Max() + 1, "false"],
            [
                DivertLayer.Socket,
                (
                    DivertFilter.Event == DivertEvent.SocketBind
                    | DivertFilter.Event == DivertEvent.SocketConnect
                    | DivertFilter.Event == DivertEvent.SocketListen
                    | DivertFilter.Event == DivertEvent.SocketAccept
                    | DivertFilter.Event == DivertEvent.SocketClose
                ),
                "event = BIND or event = CONNECT or event = LISTEN or event = ACCEPT or event = CLOSE",
            ],
            [DivertLayer.Network, DivertFilter.Event == DivertEvent.NetworkPacket, "event = PACKET"],
            [DivertLayer.Forward, DivertFilter.Event != DivertEvent.NetworkPacket, "event != PACKET"],
            [
                DivertLayer.Flow,
                (DivertFilter.Event == DivertEvent.FlowEstablished | DivertFilter.Event == DivertEvent.FlowDeleted),
                "event = ESTABLISHED or event = DELETED",
            ],
        ];
    }

    [TestMethod]
    [DynamicData(nameof(FormatFilterCases))]
    public async Task FormatFilter(DivertLayer layer, DivertFilter filter, string expected)
    {
        using var reflectService = new DivertService(
            DivertFilter.Event == DivertEvent.ReflectOpen
                & DivertFilter.ProcessId == Environment.ProcessId
                & (
                    DivertFilter.Layer == DivertLayer.Network
                    | DivertFilter.Layer == DivertLayer.Socket
                    | DivertFilter.Layer == DivertLayer.Forward
                    | DivertFilter.Layer == DivertLayer.Flow
                    | DivertFilter.Layer != DivertLayer.Reflect
                ),
            DivertLayer.Reflect,
            priority: 2,
            DivertFlags.ReceiveOnly | DivertFlags.Sniff | DivertFlags.NoInstall
        );

        var dataBuffer = new byte[ushort.MaxValue];
        var addressBuffer = new DivertAddress[1];
        var divertReceive = reflectService.ReceiveAsync(dataBuffer, addressBuffer, Token).AsTask();
        Assert.IsFalse(divertReceive.IsCompleted);

        using var service = new DivertService(filter, layer, priority: 1, DivertFlags.Sniff | DivertFlags.ReceiveOnly);

        var divertResult = await divertReceive;
        var address = addressBuffer[0];
        Assert.AreEqual(DivertLayer.Reflect, address.Layer);
        Assert.AreEqual(DivertEvent.ReflectOpen, address.Event);
        var reflectData = address.GetReflectData();
        Assert.AreEqual(Environment.ProcessId, (int)reflectData.ProcessId);
        Assert.AreEqual(layer, reflectData.Layer);
        Assert.AreEqual(DivertFlags.Sniff | DivertFlags.ReceiveOnly, reflectData.Flags);
        Assert.AreEqual(1, reflectData.Priority);
        var packet = dataBuffer.AsSpan(0, divertResult.DataLength);
        string filterString = DivertHelper.FormatFilter(packet, reflectData.Layer);
        Assert.AreEqual(expected, filterString);
    }

    [TestMethod]
    public void InvalidFilter()
    {
        var exception = Assert.Throws<Win32Exception>(() =>
        {
            using var service = new DivertService(
                DivertFilter.Event == Enum.GetValues<DivertEvent>().Max() + 1,
                DivertLayer.Reflect
            );
        });
        Assert.AreEqual((int)WIN32_ERROR.ERROR_INVALID_PARAMETER, exception.NativeErrorCode);
        exception = Assert.Throws<Win32Exception>(() =>
        {
            using var service = new DivertService(
                DivertFilter.Layer == Enum.GetValues<DivertLayer>().Max() + 1,
                DivertLayer.Reflect
            );
        });
        Assert.AreEqual((int)WIN32_ERROR.ERROR_INVALID_PARAMETER, exception.NativeErrorCode);
    }
}
