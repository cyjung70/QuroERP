using FluentAssertions;
using QuroErp.Web.Common;

namespace QuroErp.Web.Tests.Common;

/// <summary>Return addresses stay on this site (BC-UC-001-T22, open redirect).</summary>
public class LocalUrlTests
{
    [Theory, Trait("Id", "BC-UC-001-T22")]
    [InlineData("/bc/item?id=3", "/bc/item?id=3")]
    [InlineData("/", "/")]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("bc/item", "/")]
    [InlineData("https://evil.example/", "/")]
    [InlineData("//evil.example/", "/")]
    [InlineData("/\\evil.example/", "/")]
    [InlineData("javascript:alert(1)", "/")]
    public void OrRoot_KeepsOnlyLocalPaths(string? url, string expected) =>
        LocalUrl.OrRoot(url).Should().Be(expected);
}
