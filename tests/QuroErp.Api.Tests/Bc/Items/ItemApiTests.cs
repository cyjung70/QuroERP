using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Application.Common;
using QuroErp.Contracts.Bc.Items;

namespace QuroErp.Api.Tests.Bc.Items;

/// <summary>/api/bc/items (BC-UC-009): read by any signed-in caller, change by admin only (BC-BR-214).</summary>
public class ItemApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CreateItemRequest NewItem(string itemCd) => new() { ItemCd = itemCd, ItemName = "HEX BOLT", ItemClass = "PART" };

    private static string ItemUrl(long id) => $"{ItemRoutes.Base}/{id}";

    private static string DeleteUrl(ItemDto item) =>
        $"{ItemUrl(item.Id)}?updatedAt={Uri.EscapeDataString(item.UpdatedAt.ToString("O", CultureInfo.InvariantCulture))}";

    private static async Task<ItemDto> CreateAsync(HttpClient client, string itemCd)
    {
        using var response = await client.PostAsJsonAsync(ItemRoutes.Base, NewItem(itemCd));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ItemDto>())!;
    }

    [Fact, Trait("Id", "BC-UC-009-T01")]
    public async Task Post_Admin_Returns201WithDefaultsAndLocation()
    {
        var client = await factory.AdminAsync();

        using var response = await client.PostAsJsonAsync(ItemRoutes.Base, NewItem("T01") with { UsdPrc = 3m });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var item = await response.Content.ReadFromJsonAsync<ItemDto>();
        item!.ItemType.Should().Be("INVENTORY");
        item.Unit.Should().Be("EA");
        item.ValidFlag.Should().Be("Y");
        item.MvgCost.Should().Be(3m);
        item.OnhandQty.Should().Be(0);
        item.ItemGroupCd.Should().Be("T01");
        response.Headers.Location!.ToString().Should().EndWith(ItemUrl(item.Id));
        var read = await factory.NonAdmin().GetFromJsonAsync<ItemDto>(ItemUrl(item.Id));
        read.Should().BeEquivalentTo(item);
    }

    [Fact, Trait("Id", "BC-UC-009-T11")]
    public async Task Get_ListByCodePrefix_ReturnsPageAndTotal()
    {
        var client = await factory.AdminAsync();
        await CreateAsync(client, "LST2");
        await CreateAsync(client, "LST1");

        var page = await factory.NonAdmin().GetFromJsonAsync<ItemListDto>($"{ItemRoutes.Base}?itemCd=lst&size=1");

        page!.Total.Should().Be(2);
        page.Items.Select(i => i.ItemCd).Should().Equal("LST1");
    }

    [Fact, Trait("Id", "BC-UC-009-T03")]
    public async Task Post_MissingName_Returns422MmE802()
    {
        var client = await factory.AdminAsync();

        using var response = await client.PostAsJsonAsync(ItemRoutes.Base, NewItem("T02") with { ItemName = null });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be("MM-E-802");
        problem.GetProperty("args")[0].GetString().Should().Be("ItemName");
    }

    [Theory, Trait("Id", "BC-UC-009-T05")]
    [InlineData("UsdPrc")]
    [InlineData("SoUsdPrc")]
    [InlineData("ReorderPoint")]
    public async Task Post_NegativePriceOrReorderPoint_Returns422MmE803_AndSavesNothing(string field)
    {
        var client = await factory.AdminAsync();
        var request = field switch
        {
            "UsdPrc" => NewItem("NEG1") with { UsdPrc = -0.01m },
            "SoUsdPrc" => NewItem("NEG2") with { SoUsdPrc = -1m },
            _ => NewItem("NEG3") with { ReorderPoint = -1m },
        };

        using var response = await client.PostAsJsonAsync(ItemRoutes.Base, request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be("MM-E-803");
        problem.GetProperty("args")[0].GetString().Should().Be(field);
        var list = await client.GetFromJsonAsync<ItemListDto>($"{ItemRoutes.Base}?itemCd={request.ItemCd}");
        list!.Items.Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UC-009-T04")]
    public async Task Post_UsedCode_Returns422MmE801()
    {
        var client = await factory.AdminAsync();
        await CreateAsync(client, "DUP");

        using var response = await client.PostAsJsonAsync(ItemRoutes.Base, NewItem("DUP"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("MM-E-801");
    }

    [Fact, Trait("Id", "CORE-020-T01"), Trait("Id", "BC-UC-009-T12")]
    public async Task PreviousNext_FollowItemClassThenCode_And204AtTheEnd()
    {
        var client = await factory.AdminAsync();
        var first = (await (await client.PostAsJsonAsync(ItemRoutes.Base, NewItem("NAV2") with { ItemClass = "ZZNAV1" }))
            .Content.ReadFromJsonAsync<ItemDto>())!;
        var second = (await (await client.PostAsJsonAsync(ItemRoutes.Base, NewItem("NAV1") with { ItemClass = "ZZNAV2" }))
            .Content.ReadFromJsonAsync<ItemDto>())!;
        var reader = factory.NonAdmin();

        var next = await reader.GetFromJsonAsync<ItemDto>($"{ItemUrl(first.Id)}/{ItemRoutes.Next}");
        var previous = await reader.GetFromJsonAsync<ItemDto>($"{ItemUrl(second.Id)}/{ItemRoutes.Previous}");
        using var last = await reader.GetAsync($"{ItemUrl(second.Id)}/{ItemRoutes.Next}");
        using var unknown = await reader.GetAsync($"{ItemUrl(999_999)}/{ItemRoutes.Next}");

        next!.ItemCd.Should().Be("NAV1");
        previous!.ItemCd.Should().Be("NAV2");
        last.StatusCode.Should().Be(HttpStatusCode.NoContent);
        unknown.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact, Trait("Id", "BC-UC-009-T13")]
    public async Task Put_StaleUpdatedAt_Returns409CommonE004_CurrentSaves()
    {
        var client = await factory.AdminAsync();
        var item = await CreateAsync(client, "T03");
        var change = new UpdateItemRequest
        {
            ItemName = "NUT",
            ItemType = item.ItemType,
            Unit = item.Unit,
            ItemClass = item.ItemClass,
            ValidFlag = item.ValidFlag,
            UsdPrc = 9m,
            UpdatedAt = item.UpdatedAt,
        };

        using var saved = await client.PutAsJsonAsync(ItemUrl(item.Id), change);
        using var stale = await client.PutAsJsonAsync(ItemUrl(item.Id), change);

        saved.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await saved.Content.ReadFromJsonAsync<ItemDto>();
        dto!.ItemName.Should().Be("NUT");
        dto.MvgCost.Should().Be(item.MvgCost);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await stale.ReadProblemAsync()).GetProperty("code").GetString().Should().Be(ConcurrencyException.Code);
    }

    [Fact, Trait("Id", "MM-UT-802")]
    public async Task Post_UnknownUnitCode_Returns422CommonE002()
    {
        var client = await factory.AdminAsync();

        using var response = await client.PostAsJsonAsync(ItemRoutes.Base, NewItem("T04") with { Unit = "BOX" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be("Common-E-002");
        problem.GetProperty("args")[0].GetString().Should().Be("UNIT");
    }

    [Fact, Trait("Id", "BC-UC-009-T06")]
    public async Task Put_TypeOfLockedItem_Returns422MmE806()
    {
        var client = await factory.AdminAsync();
        var item = await CreateAsync(client, "LOCK");
        factory.Items.TypeLocked.Add(item.Id);

        using var response = await client.PutAsJsonAsync(ItemUrl(item.Id), new UpdateItemRequest
        {
            ItemName = item.ItemName,
            ItemType = "SERVICE",
            Unit = item.Unit,
            ItemClass = item.ItemClass,
            ValidFlag = item.ValidFlag,
            UsdPrc = item.UsdPrc,
            UpdatedAt = item.UpdatedAt,
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("MM-E-806");
    }

    [Fact, Trait("Id", "BC-UC-009-T07")]
    public async Task Delete_UsedItem_Returns422MmE807_UnusedReturns204()
    {
        var client = await factory.AdminAsync();
        var used = await CreateAsync(client, "USED");
        var unused = await CreateAsync(client, "UNUSED");
        factory.Items.InUse.Add(used.Id);

        using var blocked = await client.DeleteAsync(DeleteUrl(used));
        using var deleted = await client.DeleteAsync(DeleteUrl(unused));
        using var gone = await client.GetAsync(ItemUrl(unused.Id));

        blocked.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await blocked.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("MM-E-807");
        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        gone.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, Trait("Id", "BC-UT-214")]
    public async Task Post_NonAdmin_Returns403()
    {
        using var response = await factory.NonAdmin().PostAsJsonAsync(ItemRoutes.Base, NewItem("NOPE"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
