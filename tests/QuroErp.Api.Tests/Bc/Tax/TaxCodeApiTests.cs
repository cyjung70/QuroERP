using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Contracts.Bc.Tax;
using QuroErp.Contracts.Common;
using static QuroErp.Api.Tests.Bc.Tax.InMemoryTaxCodeRepository;

namespace QuroErp.Api.Tests.Bc.Tax;

/// <summary>/api/bc/tax-codes (BC-UC-028, BC-BR-507) for the KR company. Saves use their own factory so the shared codes stay as seeded.</summary>
public class TaxCodeApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact, Trait("Id", "BC-UC-028")]
    public async Task Search_ForAnySignedInCaller_ReturnsCodesWithNamesInTheLanguage()
    {
        var codes = await factory.NonAdmin().GetFromJsonAsync<List<TaxCodeDto>>(TaxCodeRoutes.Base + "?lang=ko");

        codes!.Select(t => (t.TaxCode, t.Name, t.TaxKind, t.Rate, t.InUseByDocuments)).Should().Equal(
            ("NONE", "세금 없음", "NONE", 0m, false), ("VAT0", "영세율", "VAT_ZERO", 0m, false), ("VAT10", "과세 10%", "VAT_STANDARD", 10m, true));
    }

    [Fact, Trait("Id", "BC-UT-507"), Trait("Rule", "BC-BR-507")]
    public async Task Save_WithoutFullAccess_Returns403()
    {
        using var response = await factory.NonAdmin().PostAsJsonAsync(TaxCodeRoutes.Batch, Batch());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, Trait("Id", "BC-UC-028")]
    public async Task Save_AddsAndChanges_AndReturnsEveryCode()
    {
        await using var own = new ApiFactory();
        var client = await own.AdminAsync();

        using var response = await client.PostAsJsonAsync(TaxCodeRoutes.Batch + "?lang=en", Batch(
            added: [new TaxCodeSaveRow(null, "vatex", "VAT_EXEMPT", null, null, null, null, En("Exempt"), null)],
            updated: [new TaxCodeSaveRow(Vat0Id, "VAT0", "VAT_ZERO", null, null, null, false, En("Zero rate (old)"), Created)]));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var codes = await response.Content.ReadFromJsonAsync<List<TaxCodeDto>>();
        codes!.Select(t => (t.TaxCode, t.Name, t.UseYn)).Should().Equal(
            ("NONE", "No tax", true), ("VAT0", "Zero rate (old)", false), ("VAT10", "VAT 10%", true), ("VATEX", "Exempt", true));
    }

    [Theory, Trait("Id", "BC-UC-028-T01"), Trait("Id", "BC-UC-028-T02")]
    [InlineData("SALES_TAX", "1", false, "BC-E-502")]
    [InlineData("VAT_STANDARD", "100", false, "BC-E-503")]
    [InlineData("VAT_ZERO", "0", true, "BC-E-504")]
    public async Task Save_ABadKindRateOrPriceIncluded_Returns422_WithTheCode(string kind, string rate, bool priceIncluded, string error)
    {
        await using var own = new ApiFactory();
        var client = await own.AdminAsync();

        using var response = await client.PostAsJsonAsync(TaxCodeRoutes.Batch, Batch(
            added: [new TaxCodeSaveRow(null, "T1", kind, decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture), null, priceIncluded, null, En("X"), null)]));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be(error);
        problem.GetProperty("args")[0].GetString().Should().Be("T1");
    }

    [Fact, Trait("Id", "BC-UC-028-T03"), Trait("Rule", "BC-BR-506")]
    public async Task Save_TheRateOfACodeDocumentsUse_Returns409BcE506()
    {
        await using var own = new ApiFactory();
        var client = await own.AdminAsync();

        using var response = await client.PostAsJsonAsync(TaxCodeRoutes.Batch, Batch(
            updated: [new TaxCodeSaveRow(Vat10Id, "VAT10", "VAT_STANDARD", 12, null, null, null, En("VAT 12%"), Created)]));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be("BC-E-506");
        problem.GetProperty("args")[0].GetString().Should().Be("VAT10");
    }

    [Fact, Trait("Id", "BC-UC-028-T04"), Trait("Rule", "BC-BR-505")]
    public async Task Delete_None_Returns409BcE505()
    {
        await using var own = new ApiFactory();
        var client = await own.AdminAsync();

        using var response = await client.PostAsJsonAsync(TaxCodeRoutes.Batch, Batch(deleted: [new TaxCodeKey(NoneId, Created)]));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("BC-E-505");
    }

    private static BatchRequest<TaxCodeSaveRow, TaxCodeKey> Batch(
        TaxCodeSaveRow[]? added = null, TaxCodeSaveRow[]? updated = null, TaxCodeKey[]? deleted = null) =>
        new(added ?? [], updated ?? [], deleted ?? []);

    private static Dictionary<string, string?> En(string name) => new() { ["en"] = name };
}
