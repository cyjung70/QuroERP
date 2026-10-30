using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Contracts.Bc.Items;

namespace QuroErp.Api.Tests.Bc.Items;

/// <summary>/api/bc/item-barcodes (BC-UC-011): read by any signed-in caller, generation by admin only. Each test uses its own item class.</summary>
public class ItemBarcodeApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<ItemDto> CreateAsync(HttpClient client, string itemCd, string itemClass, string? upcCode = null, string validFlag = "Y")
    {
        using var response = await client.PostAsJsonAsync(ItemRoutes.Base, new CreateItemRequest
        {
            ItemCd = itemCd,
            ItemName = "HEX BOLT",
            ItemClass = itemClass,
            UpcCode = upcCode,
            ValidFlag = validFlag,
            SoUsdPrc = 4m,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ItemDto>())!;
    }

    private static string Query(string itemClass, string? validFlag = null, string? startNo = null) =>
        $"?itemClass={itemClass}" + (validFlag is null ? "" : $"&validFlag={validFlag}") + (startNo is null ? "" : $"&startNo={startNo}");

    private static async Task<IReadOnlyList<ItemBarcodeDto>> ListAsync(HttpClient client, string itemClass, string? validFlag = null) =>
        (await client.GetFromJsonAsync<List<ItemBarcodeDto>>(ItemBarcodeRoutes.Base + Query(itemClass, validFlag)))!;

    private static Task<HttpResponseMessage> GenerateAsync(HttpClient client, string itemClass, string? startNo, string? validFlag = null) =>
        client.PostAsJsonAsync($"{ItemBarcodeRoutes.Base}/{ItemBarcodeRoutes.Generate}", new GenerateItemBarcodesRequest(itemClass, validFlag, startNo));

    [Fact, Trait("Id", "BC-UC-011-T01"), Trait("Id", "MM-UT-815")]
    public async Task Generate_GivesItemsInItemCodeOrderStartNoPlusOne_AndListShowsThem()
    {
        var client = await factory.AdminAsync();
        await CreateAsync(client, "BC1-B", "ZZBC1");
        await CreateAsync(client, "BC1-A", "ZZBC1");
        await CreateAsync(client, "BC1-N", "ZZBC1", validFlag: "N");

        var preview = await client.GetFromJsonAsync<ItemBarcodePreviewDto>(
            $"{ItemBarcodeRoutes.Base}/{ItemBarcodeRoutes.Preview}" + Query("ZZBC1"));
        using var generated = await GenerateAsync(client, "ZZBC1", startNo: null);
        var list = await ListAsync(factory.NonAdmin(), "ZZBC1");

        preview!.TargetCount.Should().Be(2);
        preview.Items.Select(i => (i.ItemCd, i.UpcCode)).Should().Equal(("BC1-A", "1000000001"), ("BC1-B", "1000000002"));
        preview.Conflicts.Should().BeEmpty();
        generated.StatusCode.Should().Be(HttpStatusCode.OK);
        (await generated.Content.ReadFromJsonAsync<GenerateItemBarcodesResultDto>())!.UpdatedCount.Should().Be(2);
        list.Should().Equal(
            new ItemBarcodeDto(list[0].Id, "BC1-A", "HEX BOLT", 4m, "1000000001"),
            new ItemBarcodeDto(list[1].Id, "BC1-B", "HEX BOLT", 4m, "1000000002"));
        (await ListAsync(client, "ZZBC1", ItemBarcodeRoutes.AllValidFlags)).Select(i => i.UpcCode).Should().Equal("1000000001", "1000000002", null);
    }

    [Fact, Trait("Id", "BC-UC-011-T02"), Trait("Id", "MM-UT-815")]
    public async Task Generate_OverwritesExistingBarcodes_WhichThePreviewCounts()
    {
        var client = await factory.AdminAsync();
        await CreateAsync(client, "BC2-A", "ZZBC2", upcCode: "2000000002");
        await CreateAsync(client, "BC2-B", "ZZBC2", upcCode: "2000000001");
        await CreateAsync(client, "BC2-C", "ZZBC2");

        var preview = await client.GetFromJsonAsync<ItemBarcodePreviewDto>(
            $"{ItemBarcodeRoutes.Base}/{ItemBarcodeRoutes.Preview}" + Query("ZZBC2", startNo: "2000000000"));
        using var generated = await GenerateAsync(client, "ZZBC2", "2000000000");

        preview!.Should().BeEquivalentTo(new { TargetCount = 3, OverwriteCount = 2, Conflicts = Array.Empty<object>() });
        generated.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ListAsync(client, "ZZBC2")).Select(i => i.UpcCode).Should().Equal("2000000001", "2000000002", "2000000003");
    }

    [Fact, Trait("Id", "BC-UC-011-T03"), Trait("Id", "MM-UT-816")]
    public async Task OverTenDigitsOrNotDigits_Returns422MmE816_AndChangesNothing()
    {
        var client = await factory.AdminAsync();
        await CreateAsync(client, "BC3-A", "ZZBC3", upcCode: "3000000001");
        await CreateAsync(client, "BC3-B", "ZZBC3");

        using var preview = await client.GetAsync($"{ItemBarcodeRoutes.Base}/{ItemBarcodeRoutes.Preview}" + Query("ZZBC3", startNo: "9999999998"));
        using var generated = await GenerateAsync(client, "ZZBC3", "9999999998");
        using var letters = await GenerateAsync(client, "ZZBC3", "12AB");

        foreach (var response in new[] { preview, generated, letters })
        {
            response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("MM-E-816");
        }
        (await ListAsync(client, "ZZBC3")).Select(i => i.UpcCode).Should().Equal("3000000001", null);
    }

    [Fact, Trait("Id", "BC-UC-011-T04"), Trait("Id", "BC-UC-011-T05"), Trait("Id", "MM-UT-817"), Trait("Id", "MM-UT-818")]
    public async Task BarcodeOfAnotherItem_PreviewShowsIt_AndGenerateReturns422MmE817_ChangingNothing()
    {
        var client = await factory.AdminAsync();
        await CreateAsync(client, "BC4-A", "ZZBC4", upcCode: "4000000009");
        await CreateAsync(client, "BC4-B", "ZZBC4");
        await CreateAsync(client, "BC4-OTHER", "PART", upcCode: "4000000002");

        var preview = await client.GetFromJsonAsync<ItemBarcodePreviewDto>(
            $"{ItemBarcodeRoutes.Base}/{ItemBarcodeRoutes.Preview}" + Query("ZZBC4", startNo: "4000000000"));
        using var generated = await GenerateAsync(client, "ZZBC4", "4000000000");

        preview!.Conflicts.Should().Equal(new ItemBarcodeConflictDto("BC4-B", "4000000002", "BC4-OTHER"));
        generated.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await generated.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be("MM-E-817");
        problem.GetProperty("args")[0].GetString().Should().Be("BC4-OTHER");
        (await ListAsync(client, "ZZBC4")).Select(i => i.UpcCode).Should().Equal("4000000009", null);
    }

    [Fact, Trait("Id", "MM-UT-817")]
    public async Task ItemSave_BarcodeOfAnotherItem_Returns422MmE817_ButItsOwnBarcodeIsFine()
    {
        var client = await factory.AdminAsync();
        await CreateAsync(client, "BC5-A", "ZZBC5", upcCode: "5000000001");
        var b = await CreateAsync(client, "BC5-B", "ZZBC5", upcCode: "5000000002");

        using var created = await client.PostAsJsonAsync(ItemRoutes.Base, new CreateItemRequest
        {
            ItemCd = "BC5-C",
            ItemName = "NUT",
            ItemClass = "ZZBC5",
            UpcCode = "5000000001",
        });
        using var changed = await client.PutAsJsonAsync($"{ItemRoutes.Base}/{b.Id}", Update(b, "5000000001"));
        using var kept = await client.PutAsJsonAsync($"{ItemRoutes.Base}/{b.Id}", Update(b, "5000000002"));

        foreach (var response in new[] { created, changed })
        {
            response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            var problem = await response.ReadProblemAsync();
            problem.GetProperty("code").GetString().Should().Be("MM-E-817");
            problem.GetProperty("args")[0].GetString().Should().Be("BC5-A");
        }
        kept.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, Trait("Id", "BC-UT-214")]
    public async Task Generate_NonAdmin_Returns403_AndMissingItemClass_Returns422MmE802()
    {
        using var forbidden = await GenerateAsync(factory.NonAdmin(), "ZZBC1", "1");
        using var noClass = await (await factory.AdminAsync()).GetAsync(ItemBarcodeRoutes.Base);

        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        noClass.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await noClass.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be("MM-E-802");
        problem.GetProperty("args")[0].GetString().Should().Be("ItemClass");
    }

    private static UpdateItemRequest Update(ItemDto item, string upcCode) => new()
    {
        ItemName = item.ItemName,
        ItemType = item.ItemType,
        Unit = item.Unit,
        ItemClass = item.ItemClass,
        ValidFlag = item.ValidFlag,
        UsdPrc = item.UsdPrc,
        SoUsdPrc = item.SoUsdPrc,
        VatKind = item.VatKind,
        UpcCode = upcCode,
        UpdatedAt = item.UpdatedAt,
    };
}
