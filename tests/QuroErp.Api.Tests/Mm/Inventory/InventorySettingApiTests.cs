using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Application.Common;
using QuroErp.Contracts.Mm.Inventory;

namespace QuroErp.Api.Tests.Mm.Inventory;

/// <summary>Inventory setting API (MM-BR-003): read by any signed-in caller, change by admin only, 409 on stale updatedAt.</summary>
public class InventorySettingApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact, Trait("Id", "MM-SET-IT-10")]
    public async Task Get_NonAdmin_ReturnsSetting()
    {
        var setting = await factory.NonAdmin().GetFromJsonAsync<InventorySettingDto>(InventorySettingRoutes.Base);

        setting!.NegativeStockMode.Should().BeOneOf(NegativeStockModes.All);
    }

    [Fact, Trait("Id", "MM-SET-IT-11")]
    public async Task Put_AdminWithCurrentUpdatedAt_Returns200WithSavedSetting()
    {
        var client = await factory.AdminAsync();
        var current = await client.GetFromJsonAsync<InventorySettingDto>(InventorySettingRoutes.Base);

        using var response = await client.PutAsJsonAsync(
            InventorySettingRoutes.Base, new UpdateInventorySettingRequest(NegativeStockModes.AllowInMonth, current!.UpdatedAt));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await response.Content.ReadFromJsonAsync<InventorySettingDto>();
        saved!.NegativeStockMode.Should().Be(NegativeStockModes.AllowInMonth);
        saved.UpdatedBy.Should().Be(ApiFactory.UserId);
        saved.UpdatedAt.Should().BeAfter(current.UpdatedAt);
    }

    [Fact, Trait("Id", "MM-SET-IT-12")]
    public async Task Put_StaleUpdatedAt_Returns409CommonE004()
    {
        var client = await factory.AdminAsync();

        using var response = await client.PutAsJsonAsync(
            InventorySettingRoutes.Base, new UpdateInventorySettingRequest(NegativeStockModes.Allow, DateTimeOffset.UnixEpoch));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be(ConcurrencyException.Code);
    }

    [Fact, Trait("Id", "MM-SET-IT-13")]
    public async Task Put_UnknownMode_Returns400()
    {
        var client = await factory.AdminAsync();

        using var response = await client.PutAsJsonAsync(
            InventorySettingRoutes.Base, new UpdateInventorySettingRequest("MAYBE", DateTimeOffset.UnixEpoch));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, Trait("Id", "MM-SET-IT-14")]
    public async Task Put_NonAdmin_Returns403()
    {
        using var response = await factory.NonAdmin().PutAsJsonAsync(
            InventorySettingRoutes.Base, new UpdateInventorySettingRequest(NegativeStockModes.Allow, DateTimeOffset.UnixEpoch));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
