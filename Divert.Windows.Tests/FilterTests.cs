namespace Divert.Windows.Tests;

[TestClass]
public class FilterTests
{
    public static IEnumerable<object[]> FilterCases()
    {
        return
        [
            [(DivertFilter.TCP | DivertFilter.UDP), "tcp or udp"],
            [(DivertFilter.TCP & DivertFilter.Loopback), "tcp and loopback"],
            [(DivertFilter.TCP | DivertFilter.UDP) & DivertFilter.Outbound, "(tcp or udp) and outbound"],
            [DivertFilter.Outbound & (DivertFilter.TCP | DivertFilter.UDP), "outbound and (tcp or udp)"],
            [
                (DivertFilter.TCP & (DivertFilter.RemotePort == "80" | DivertFilter.RemotePort == "443"))
                    | DivertFilter.Outbound,
                "(tcp and (remotePort = 80 or remotePort = 443)) or outbound",
            ],
            [
                DivertFilter.Outbound
                    & (DivertFilter.TCP | DivertFilter.UDP)
                    & (DivertFilter.RemotePort == "80" | DivertFilter.RemotePort == "443"),
                "outbound and (tcp or udp) and (remotePort = 80 or remotePort = 443)",
            ],
            [
                !DivertFilter.Loopback | (DivertFilter.TCP & DivertFilter.RemotePort == "443"),
                "not loopback or (tcp and remotePort = 443)",
            ],
            [
                DivertFilter.Loopback | (!DivertFilter.TCP & DivertFilter.RemotePort != "443"),
                "loopback or (not tcp and remotePort != 443)",
            ],
            [
                DivertFilter.Inbound & (DivertFilter.LocalPort > "30000" | DivertFilter.LocalPort < "1024"),
                "inbound and (localPort > 30000 or localPort < 1024)",
            ],
            [
                DivertFilter.RemotePort >= "5000" | DivertFilter.RemotePort <= "6000",
                "remotePort >= 5000 or remotePort <= 6000",
            ],
            [
                DivertFilter.Packet(10) == "0x1"
                    | DivertFilter.Packet16(20) == "0x2"
                    | DivertFilter.Packet32(30) == "0x3",
                "packet[10] = 0x1 or packet16[20] = 0x2 or packet32[30] = 0x3",
            ],
        ];
    }

    [TestMethod]
    [DynamicData(nameof(FilterCases))]
    public void FilterToString(DivertFilter filter, string expected)
    {
        var result = filter.ToString();
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void InvalidFilter()
    {
        // Unmatched parentheses
        Assert.Throws<InvalidOperationException>(() => DivertFilter.Outbound & "(tcp or udp))");
        Assert.Throws<InvalidOperationException>(() => DivertFilter.Outbound & "((tcp or udp)");
    }

    [TestMethod]
    public void Equals()
    {
        var filter = new DivertFilter("(tcp or udp) and outbound");
        Assert.IsTrue(filter.Equals(filter));
        Assert.IsTrue(filter.Equals("(tcp or udp) and outbound"));
        Assert.IsTrue(filter.Equals((DivertFilter.TCP | DivertFilter.UDP) & DivertFilter.Outbound));
        Assert.IsFalse(filter.Equals(DivertFilter.TCP));
        Assert.IsFalse(filter.Equals(null));
        Assert.IsFalse(new DivertFilter("5").Equals(5));
        Assert.AreEqual(filter.GetHashCode(), filter.ToString().GetHashCode());

        var field = new DivertFilter.Field("tcp");
        Assert.IsTrue(field.Equals(field));
        Assert.IsTrue(field.Equals("tcp"));
        Assert.IsTrue(field.Equals(DivertFilter.TCP));
        Assert.IsFalse(field.Equals("udp"));
        Assert.IsFalse(field.Equals(DivertFilter.UDP));
        Assert.IsFalse(field.Equals(null));
        Assert.IsFalse(new DivertFilter.Field("5").Equals(5));
        Assert.AreEqual(field.GetHashCode(), field.ToString().GetHashCode());
    }
}
