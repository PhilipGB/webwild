using Webwild.Core;
using Xunit;

namespace Webwild.Core.Tests;

public class BootstrapTests
{
    [Fact]
    public void BuildInfo_IdentifiesWebwild()
    {
        Assert.Equal("Webwild", BuildInfo.GameName);
        Assert.False(string.IsNullOrWhiteSpace(BuildInfo.CoreVersion));
    }
}
