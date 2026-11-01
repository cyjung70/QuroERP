using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Contracts.Bc.Localization;
using QuroErp.Contracts.Common;
using static QuroErp.Api.Tests.Bc.Localization.InMemoryExchangeRateRepository;

namespace QuroErp.Api.Tests.Bc.Localization;

/// <summary>
/// /api/bc/exchange-rates (BC-UC-024, BC-BR-214) for the KR company (base KRW, USD in use). Saves use their own factory so the
/// shared rates stay as seeded.
/// </summary>
public class ExchangeRateApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    internal const string October = "?from=2026-10-01&to=2026-10-31";

    private static readonly DateOnly Oct1 = new(2026, 10, 1);
    private static readonly DateOnly Oct3 = new(2026, 10, 3);

    [Fact, Trait("Id", "BC-UC-024"), Trait("Rule", "SD-BR-908")]
    public async Task Search_ForAnySignedInCaller_ReturnsThePeriodNewestFirst()
    {
        var rates = await factory.NonAdmin().GetFromJsonAsync<List<ExchangeRateDto>>(ExchangeRateRoutes.Base + October + "&currencyCode=usd");

        rates!.Select(r => (r.Id, r.RateDt, r.Rate)).Should().Equal((Oct2Id, new DateOnly(2026, 10, 2), 1352.5m), (Oct1Id, Oct1, 1350m));
    }

    [Fact, Trait("Id", "BC-UT-214")]
    public async Task Save_WithoutFullAccess_Returns403()
    {
        using var response = await factory.NonAdmin().PostAsJsonAsync(ExchangeRateRoutes.Batch, Batch());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, Trait("Id", "BC-UC-024-T06")]
    public async Task Save_AddsChangesAndDeletes_AndReturnsThePeriod()
    {
        await using var own = new ApiFactory();
        var client = await own.AdminAsync();

        using var response = await client.PostAsJsonAsync(ExchangeRateRoutes.Batch + October, Batch(
            added: [new ExchangeRateSaveRow(null, Oct3, "usd", 1349.123456m, null)],
            updated: [new ExchangeRateSaveRow(Oct1Id, Oct1, "USD", 1351m, Created)],
            deleted: [new ExchangeRateKey(Oct2Id, Created)]));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rates = await response.Content.ReadFromJsonAsync<List<ExchangeRateDto>>();
        rates!.Select(r => (r.RateDt, r.CurrencyCode, r.Rate)).Should().Equal((Oct3, "USD", 1349.123456m), (Oct1, "USD", 1351m));
    }

    [Theory, Trait("Id", "BC-UC-024-T04"), Trait("Id", "BC-UC-024-T05"), Trait("Rule", "SD-BR-907")]
    [InlineData("KRW", "1")]
    [InlineData("JPY", "9")]
    [InlineData("USD", "0")]
    [InlineData("USD", "0.00000000001")]
    public async Task Save_BaseOrUnusedCurrency_OrABadRate_Returns422SdE907_WithTheRow(string code, string rate)
    {
        await using var own = new ApiFactory();
        var client = await own.AdminAsync();

        using var response = await client.PostAsJsonAsync(ExchangeRateRoutes.Batch, Batch(
            added: [new ExchangeRateSaveRow(null, Oct3, code, decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture), null)]));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be("SD-E-907");
        problem.GetProperty("args")[0].GetString().Should().Be("2026-10-03");
        problem.GetProperty("args")[1].GetString().Should().Be(code);
    }

    [Fact, Trait("Id", "BC-UC-024-T01"), Trait("Rule", "SD-BR-906")]
    public async Task Save_AStoredDateAndCurrency_Returns422SdE906_AndSavesNothing()
    {
        await using var own = new ApiFactory();
        var client = await own.AdminAsync();

        using var response = await client.PostAsJsonAsync(ExchangeRateRoutes.Batch, Batch(
            added: [new ExchangeRateSaveRow(null, Oct3, "USD", 1m, null), new ExchangeRateSaveRow(null, Oct1, "USD", 1m, null)]));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("SD-E-906");
        var rates = await client.GetFromJsonAsync<List<ExchangeRateDto>>(ExchangeRateRoutes.Base + October);
        rates!.Select(r => r.Id).Should().Equal(Oct2Id, Oct1Id);
    }

    [Fact, Trait("Id", "SD-UT-905"), Trait("Rule", "SD-BR-905")]
    public async Task Save_AStaleRow_Returns409CommonE004()
    {
        await using var own = new ApiFactory();
        var client = await own.AdminAsync();

        using var response = await client.PostAsJsonAsync(ExchangeRateRoutes.Batch, Batch(
            updated: [new ExchangeRateSaveRow(Oct1Id, Oct1, "USD", 1m, Created.AddSeconds(-1))]));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("Common-E-004");
    }

    private static BatchRequest<ExchangeRateSaveRow, ExchangeRateKey> Batch(
        ExchangeRateSaveRow[]? added = null, ExchangeRateSaveRow[]? updated = null, ExchangeRateKey[]? deleted = null) =>
        new(added ?? [], updated ?? [], deleted ?? []);
}
