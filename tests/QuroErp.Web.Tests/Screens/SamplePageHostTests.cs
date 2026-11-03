using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using QuroErp.Web.Auth;
using QuroErp.Web.Components.Pages.Samples;

namespace QuroErp.Web.Tests.Screens;

/// <summary>The screen type samples exist only in Development and need sign-in like every other page.</summary>
public class SamplePageHostTests(WebFactory factory) : IClassFixture<WebFactory>
{
    [Fact]
    public void SampleStore_OnlyInDevelopment()
    {
        factory.Services.GetService<SampleStore>().Should().NotBeNull();
        factory.Services.GetService<SamplePoStore>().Should().NotBeNull();

        using var production = factory.WithWebHostBuilder(b => b.UseEnvironment("Production"));
        production.Services.GetService<SampleStore>().Should().BeNull();
        production.Services.GetService<SamplePoStore>().Should().BeNull();
    }

    [Theory]
    [InlineData("/samples/single")]
    [InlineData("/samples/multi")]
    [InlineData("/samples/header-detail")]
    [InlineData("/samples/master-detail")]
    [InlineData("/samples/po-entry")]
    [InlineData("/samples/free-form")]
    public async Task SamplePage_Anonymous_RedirectsToLogin(string path)
    {
        var response = await factory.CreateBrowser().GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain(WebAuthentication.LoginPath);
    }
}
