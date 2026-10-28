using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Application.Common;
using QuroErp.Contracts.Bc.Localization;

namespace QuroErp.Api.Tests.Bc.Localization;

/// <summary>GET, PUT /api/bc/localization-setting (BC-UC-027): read by any signed-in caller, change by admin only (BC-BR-427).</summary>
public class LocalizationSettingApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact, Trait("Id", "BC-UC-027-IT-10")]
    public async Task Get_NonAdmin_ReturnsEveryLanguageAndDecimals()
    {
        var dto = await factory.NonAdmin().GetFromJsonAsync<LocalizationSettingDto>(LocalizationRoutes.Setting);

        dto!.CountryCode.Should().Be("KR");
        dto.Languages.Should().Contain(new LanguageSettingDto("sw", false));
        dto.CountryDecimals.Should().Contain(new CountryDecimalDto("KR", "MM", 2));
        dto.DecimalSettings.Should().HaveCount(2);
    }

    [Fact, Trait("Id", "BC-UC-027-IT-11")]
    public async Task Put_Admin_Returns200AndLocalizationShowsNewValues()
    {
        var client = await factory.AdminAsync();
        var current = await client.GetFromJsonAsync<LocalizationSettingDto>(LocalizationRoutes.Setting);

        using var response = await client.PutAsJsonAsync(
            LocalizationRoutes.Setting,
            new UpdateLocalizationSettingRequest([], [new("US", "SD", 3)], [], "LINE", current!.UpdatedAt));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await response.Content.ReadFromJsonAsync<LocalizationSettingDto>();
        saved!.CountryDecimals.Should().Contain(new CountryDecimalDto("US", "SD", 3));
        saved.TaxCalcMode.Should().Be("LINE");
        saved.UpdatedAt.Should().BeAfter(current.UpdatedAt);
        var localization = await client.GetFromJsonAsync<LocalizationDto>(LocalizationRoutes.Base);
        localization!.CountryDecimals.Should().Contain(new CountryDecimalDto("US", "SD", 3));
    }

    [Fact, Trait("Id", "BC-UC-027-IT-12")]
    public async Task Put_EnOff_Returns422BcE401()
    {
        var client = await factory.AdminAsync();
        var current = await client.GetFromJsonAsync<LocalizationSettingDto>(LocalizationRoutes.Setting);

        using var response = await client.PutAsJsonAsync(
            LocalizationRoutes.Setting, new UpdateLocalizationSettingRequest([new("en", false)], [], [], "DOCUMENT", current!.UpdatedAt));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("BC-E-401");
    }

    [Fact, Trait("Id", "BC-UC-027-IT-13")]
    public async Task Put_ReduceDecimalsDocumentsUse_Returns409BcE425()
    {
        factory.LocalizationSettings.ModulesWithDocuments.Add("MM");
        var client = await factory.AdminAsync();
        var current = await client.GetFromJsonAsync<LocalizationSettingDto>(LocalizationRoutes.Setting);

        using var response = await client.PutAsJsonAsync(
            LocalizationRoutes.Setting, new UpdateLocalizationSettingRequest([], [new("KR", "MM", 1)], [], "DOCUMENT", current!.UpdatedAt));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be("BC-E-425");
        problem.GetProperty("args")[0].GetString().Should().Be("KR MM");
    }

    [Fact, Trait("Id", "BC-UC-027-IT-14")]
    public async Task Put_StaleUpdatedAt_Returns409CommonE004()
    {
        var client = await factory.AdminAsync();

        using var response = await client.PutAsJsonAsync(
            LocalizationRoutes.Setting, new UpdateLocalizationSettingRequest([], [], [], "DOCUMENT", DateTimeOffset.UnixEpoch));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be(ConcurrencyException.Code);
    }

    [Fact, Trait("Id", "BC-UC-027-IT-15")]
    public async Task Put_UnknownTaxCalcMode_Returns400()
    {
        var client = await factory.AdminAsync();

        using var response = await client.PutAsJsonAsync(
            LocalizationRoutes.Setting, new UpdateLocalizationSettingRequest([], [], [], "ROW", DateTimeOffset.UnixEpoch));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, Trait("Id", "BC-UT-427")]
    public async Task Put_NonAdmin_Returns403()
    {
        using var response = await factory.NonAdmin().PutAsJsonAsync(
            LocalizationRoutes.Setting, new UpdateLocalizationSettingRequest([], [], [], "DOCUMENT", DateTimeOffset.UnixEpoch));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
