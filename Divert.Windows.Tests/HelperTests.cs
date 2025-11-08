using System.ComponentModel;
using Windows.Win32.Foundation;

namespace Divert.Windows.Tests;

[TestClass]
public class HelperTests : DivertTests
{
    [TestMethod]
    public void InvalidFilter()
    {
        var invalidFilter = Array.Empty<byte>();
        var exception = Assert.Throws<Win32Exception>(() =>
            DivertHelper.FormatFilter(invalidFilter, DivertLayer.Network)
        );
        Assert.AreEqual((int)WIN32_ERROR.ERROR_INVALID_PARAMETER, exception.NativeErrorCode);
    }
}
