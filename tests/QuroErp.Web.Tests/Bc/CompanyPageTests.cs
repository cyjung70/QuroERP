using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using QuroErp.Contracts.Bc.Companies;
using QuroErp.Contracts.Bc.Localization;
using QuroErp.Contracts.Common;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Contracts.Mm.Inventory;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Tests.Bc;

/// <summary>Company screen /bc/company rendered on the server: logo and inventory setting areas (BC-UC-007, BC-BR-306, MM-BR-003).</summary>
public class CompanyPageTests(WebFactory factory) : IClassFixture<WebFactory>
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Fact, Trait("Id", "BC-007-WT-01")]
    public async Task Anonymous_RedirectsToLogin()
    {
        var response = await factory.CreateBrowser().GetAsync("/bc/company");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.AbsolutePath.Should().Be(WebAuthentication.LoginPath);
    }

    [Fact, Trait("Id", "BC-007-WT-02")]
    public async Task Admin_SeesLogoUploadAndModeChoicesWithBearerToken()
    {
        var requests = new List<HttpRequestMessage>();
        factory.BusinessApiResponse = request =>
        {
            requests.Add(request);
            return Respond(request, withLogo: true);
        };
        var browser = await SignInAsync("admin");

        var html = WebUtility.HtmlDecode(await browser.GetStringAsync("/bc/company"));

        html.Should().Contain("Company Logo").And.Contain("Upload Logo").And.Contain("data:image/png;base64,");
        html.Should().Contain("Always allow").And.Contain("Allow during the month (not at month end)").And.Contain("Do not allow");
        html.Should().Contain("value=\"Quro\"").And.Contain("Lockout minutes (1-1440)").And.NotContain("page-error");
        requests.Should().OnlyContain(r => r.Headers.Authorization!.Scheme == "Bearer" && r.Headers.Authorization.Parameter == "token-admin");
    }

    [Fact, Trait("Id", "BC-007-WT-03")]
    public async Task NonAdmin_SeesCurrentModeOnlyAndNoLogoMessage()
    {
        factory.BusinessApiResponse = request => Respond(request, withLogo: false);
        var browser = await SignInAsync("user");

        var html = WebUtility.HtmlDecode(await browser.GetStringAsync("/bc/company"));

        html.Should().Contain("No logo has been uploaded. Printed documents show the company name only.");
        html.Should().Contain("Negative Stock: Do not allow");
        html.Should().NotContain("Upload Logo").And.NotContain("Always allow");
    }

    [Theory, Trait("Id", "BC-007-WT-04")]
    [InlineData("ko", "BC-E-005", "로고 파일은 1024 KB 이하여야 합니다.")]
    [InlineData("en", "BC-E-005", "The logo file must be 1024 KB or smaller.")]
    [InlineData("en", "Common-E-004", "Another user changed this data first. Reload and try again.")]
    public void ProblemMessages_NewCodes_AreFormatted(string culture, string code, string expected)
    {
        WithCulture(culture, () =>
            Messages().Get(new ApiProblem(422, code, ["1024"])).Should().Be(expected));
    }

    [Fact, Trait("Id", "BC-007-WT-05")]
    public void Resources_KoAndEnHaveTheSameKeys()
    {
        var localizer = factory.Services.GetRequiredService<IStringLocalizer<SharedResource>>();
        HashSet<string> Keys(string culture)
        {
            HashSet<string> keys = [];
            WithCulture(culture, () => keys = localizer.GetAllStrings(includeParentCultures: false).Select(s => s.Name).ToHashSet());
            return keys;
        }

        var en = Keys("en");

        en.Should().Contain(NegativeStockModes.All.Select(m => $"Mm.InventorySetting.Mode.{m}"));
        en.Should().BeEquivalentTo(Keys("ko"));
    }

    private ProblemMessages Messages() => new(factory.Services.GetRequiredService<IStringLocalizer<SharedResource>>());

    private static HttpResponseMessage Respond(HttpRequestMessage request, bool withLogo) => request.RequestUri!.AbsolutePath switch
    {
        "/" + CompanyRoutes.Logo when withLogo => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Png) { Headers = { ContentType = new("image/png") } },
        },
        "/" + CompanyRoutes.Logo => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("""{"status":404,"code":"BC-E-007"}""", null, "application/problem+json"),
        },
        "/" + InventorySettingRoutes.Base => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new InventorySettingDto(NegativeStockModes.Deny, DateTimeOffset.UnixEpoch, "system")),
        },
        "/" + CompanyRoutes.Base => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new CompanyDto("Quro", null, null, null, null, null, null, null, null, null, null, "US", "USD", "en", "America/New_York",
                "WITH_SHIPMENT", withLogo, DateTimeOffset.UnixEpoch)),
        },
        "/" + LocalizationRoutes.Base => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new LocalizationDto("US", "USD", "en", "America/New_York", "SALES_TAX", "MM/dd/yyyy", "DOCUMENT",
                [new LanguageDto("en", 1)], [new CurrencyDto("USD", "US Dollar", 2, "US")], [], [])),
        },
        "/" + AuthSettingRoutes.Base => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new AuthSettingDto(5, 30, true, true, 30, 60, true, 8, 2, 0, 0, "ADMIN_ONLY", DateTimeOffset.UnixEpoch)),
        },
        _ => new HttpResponseMessage(HttpStatusCode.NotFound),
    };

    /// <summary>Signs in through the login page with a fake login response; the page language is en.</summary>
    private async Task<HttpClient> SignInAsync(string roleCode)
    {
        factory.ApiResponse = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new LoginResponse(
                $"token-{roleCode}", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", roleCode, false, "en")),
        };
        // The session cookie is Secure, so the client must use https to send it back
        var browser = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost"),
        });
        var token = WebFactory.AntiforgeryToken(await browser.GetStringAsync(WebAuthentication.LoginPath));
        var response = await browser.PostAsync(WebAuthentication.LoginPath, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "login",
            ["Input.UserId"] = "kim",
            ["Input.Password"] = "pass",
            ["__RequestVerificationToken"] = token,
        }));
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return browser;
    }

    private static void WithCulture(string culture, Action action)
    {
        var original = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        try
        {
            action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
