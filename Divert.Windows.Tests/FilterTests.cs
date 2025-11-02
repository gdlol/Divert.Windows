namespace Divert.Windows.Tests;

[TestClass]
public class FilterTests
{
    [TestMethod]
    public void TestFilterToString()
    {
        var filter = (DivertFilter.TCP | DivertFilter.UDP) & DivertFilter.Outbound;
        var result = filter.ToString();
        Assert.AreEqual("(tcp or udp) and outbound", result);
    }
}
