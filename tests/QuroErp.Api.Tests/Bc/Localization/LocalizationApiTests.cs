using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Contracts.Bc.Localization;
using QuroErp.Contracts.Common.Auth;

namespace QuroErp.Api.Tests.Bc.Localization;

/// <summary>GET /api/bc/localization (TASK-CORE-014, BC-UC-027, BC-BR-426).</summary>
public class LocalizationApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private string? _accessToken;

    [Fact, Trait("Id", "CORE-014-IT-01")]
    public async Task Get_SignedIn_ReturnsCompanyCountryValuesAndSettingsInUse()
    {
        var client = await SignedInClientAsync();

        var dto = await client.GetFromJsonAsync<LocalizationDto>($"{LocalizationRoutes.Base}?lang=en");

        dto!.CountryCode.Should().Be("KR");
        dto.BaseCurrencyCode.Should().Be("KRW");
        dto.DefaultLangCode.Should().Be("ko");
        dto.TimeZone.Should().Be("Asia/Seoul");
        dto.TaxMethod.Should().Be("VAT");
        dto.DateFormat.Should().Be("yyyy-MM-dd");
        dto.TaxCalcMode.Should().Be("DOCUMENT");
        dto.Languages.Should().Equal(new LanguageDto("en", 1), new LanguageDto("ko", 2));
        dto.Currencies.Should().Equal(new CurrencyDto("KRW", "Korean Won", 0, "KR"), new CurrencyDto("USD", "US Dollar", 2, "US"));
        dto.CountryDecimals.Should().Equal(
            new CountryDecimalDto("KR", "MM", 2), new CountryDecimalDto("KR", "SD", 0),
            new CountryDecimalDto("US", "MM", 4), new CountryDecimalDto("US", "SD", 2));
        dto.DecimalSettings.Should().Equal(new DecimalSettingDto("MM", 2, "HALF_UP"), new DecimalSettingDto("SD", 0, "DOWN"));
    }

    [Fact, Trait("Id", "CORE-014-IT-02")]
    public async Task Get_LangWithoutTranslation_FallsBackToCompanyDefaultThenEn()
    {
        var client = await SignedInClientAsync();

        var dto = await client.GetFromJsonAsync<LocalizationDto>($"{LocalizationRoutes.Base}?lang=sw");

        dto!.Currencies.Should().Equal(new CurrencyDto("KRW", "대한민국 원", 0, "KR"), new CurrencyDto("USD", "US Dollar", 2, "US"));
    }

    [Fact, Trait("Id", "CORE-014-IT-03")]
    public async Task Get_NoToken_Returns401()
    {
        using var response = await factory.CreateClient().GetAsync(LocalizationRoutes.Base);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateClient();
        if (_accessToken is null)
        {
            using var response = await client.PostAsJsonAsync(AuthRoutes.Login, new LoginRequest(ApiFactory.UserId, ApiFactory.Password));
            response.EnsureSuccessStatusCode();
            _accessToken = (await response.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        return client;
    }
}
