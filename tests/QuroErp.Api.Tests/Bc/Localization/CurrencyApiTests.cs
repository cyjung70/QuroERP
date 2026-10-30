using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Contracts.Bc.Localization;
using QuroErp.Contracts.Common;
using static QuroErp.Api.Tests.Bc.Localization.InMemoryCurrencyRepository;

namespace QuroErp.Api.Tests.Bc.Localization;

/// <summary>/api/bc/currencies (BC-UC-026, BC-BR-214). Saves use their own factory so the shared currencies stay as seeded.</summary>
public class CurrencyApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact, Trait("Id", "BC-UC-026")]
    public async Task Search_ForAnySignedInCaller_ReturnsCurrenciesWithNamesInTheLanguage()
    {
        var currencies = await factory.NonAdmin().GetFromJsonAsync<List<CurrencyMasterDto>>(CurrencyRoutes.Base + "?lang=ko");

        currencies!.Select(c => (c.CurrencyCode, c.Name, c.Decimals, c.CountryCode, c.InUseByDocuments)).Should().Equal(
            ("KRW", "대한민국 원", 0, "KR", false), ("USD", "US Dollar", 2, "US", true));
    }

    [Fact, Trait("Id", "BC-UT-214")]
    public async Task Save_WithoutFullAccess_Returns403()
    {
        using var response = await factory.NonAdmin().PostAsJsonAsync(CurrencyRoutes.Batch, Batch());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    internal static BatchRequest<CurrencySaveRow, CurrencyKey> Batch(
        CurrencySaveRow[]? added = null, CurrencySaveRow[]? updated = null, CurrencyKey[]? deleted = null) =>
        new(added ?? [], updated ?? [], deleted ?? []);

    internal static Dictionary<string, string?> En(string name) => new() { ["en"] = name };
}

public class CurrencySaveApiTests
{
    [Fact, Trait("Id", "BC-UC-026")]
    public async Task Save_AddsAndChanges_AndReturnsEveryCurrency()
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();

        using var response = await client.PostAsJsonAsync(CurrencyRoutes.Batch + "?lang=en", CurrencyApiTests.Batch(
            added: [new CurrencySaveRow(null, "jpy", null, "jp", null, CurrencyApiTests.En("Japanese Yen"), null)],
            updated: [new CurrencySaveRow(KrwId, "KRW", 0, "KR", true, new Dictionary<string, string?> { ["en"] = "South Korean Won", ["ko"] = "원" }, Created)]));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var currencies = await response.Content.ReadFromJsonAsync<List<CurrencyMasterDto>>();
        currencies!.Select(c => (c.CurrencyCode, c.Name, c.Decimals, c.CountryCode)).Should().Equal(
            ("JPY", "Japanese Yen", 0, "JP"), ("KRW", "South Korean Won", 0, "KR"), ("USD", "US Dollar", 2, "US"));
    }

    [Theory, Trait("Id", "BC-UC-026-T01")]
    [InlineData("US", 2, null, "BC-E-411")]
    [InlineData("USDX", 2, null, "BC-E-411")]
    [InlineData("EUR", 5, null, "BC-E-411")]
    [InlineData("EUR", 2, "EUR", "BC-E-428")]
    public async Task Save_InvalidCodeDecimalsOrCountry_Returns422(string code, int decimals, string? country, string error)
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();

        using var response = await client.PostAsJsonAsync(CurrencyRoutes.Batch, CurrencyApiTests.Batch(
            added: [new CurrencySaveRow(null, code, decimals, country, null, CurrencyApiTests.En("X"), null)]));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be(error);
    }

    [Theory, Trait("Id", "BC-UC-026-T02"), Trait("Id", "BC-UC-026-T03")]
    [InlineData(0, "US", "BC-E-411")]
    [InlineData(2, "KR", "BC-E-428")]
    public async Task Save_DecimalsOrCountryOfACurrencyDocumentsUse_Returns409(int decimals, string country, string error)
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();

        using var response = await client.PostAsJsonAsync(CurrencyRoutes.Batch, CurrencyApiTests.Batch(
            updated: [new CurrencySaveRow(UsdId, "USD", decimals, country, true, CurrencyApiTests.En("US Dollar"), Created)]));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be(error);
    }

    [Fact, Trait("Id", "BC-UC-026-T04")]
    public async Task Save_WithoutAnEnName_Returns422BcE404()
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();

        using var response = await client.PostAsJsonAsync(CurrencyRoutes.Batch, CurrencyApiTests.Batch(
            added: [new CurrencySaveRow(null, "EUR", 2, null, null, new Dictionary<string, string?> { ["ko"] = "유로" }, null)]));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("BC-E-404");
    }
}
