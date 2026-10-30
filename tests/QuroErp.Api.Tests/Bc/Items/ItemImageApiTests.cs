using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Application.Bc.Items;
using QuroErp.Contracts.Bc.Items;
using QuroErp.Domain.Bc.Items;

namespace QuroErp.Api.Tests.Bc.Items;

/// <summary>/api/bc/items/{itemCd}/image (BC-UC-010): read by any signed-in caller, upload and delete by admin only (MM-BR-824).</summary>
public class ItemImageApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly byte[] LargeJpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5, 6, 7, 8];
    private static readonly byte[] SmallJpeg = [0xFF, 0xD8, 0xFF, 0xDB, 9];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01];

    private static async Task CreateAsync(HttpClient client, string itemCd)
    {
        using var response = await client.PostAsJsonAsync(
            ItemRoutes.Base, new CreateItemRequest { ItemCd = itemCd, ItemName = "HEX BOLT", ItemClass = "PART" });
        response.EnsureSuccessStatusCode();
    }

    private static MultipartFormDataContent Form(byte[] image, string fileName = "item.jpg", string contentType = "image/jpeg")
    {
        var file = new ByteArrayContent(image);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, ItemRoutes.ImageFormField, fileName } };
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response) => (await response.ReadProblemAsync()).GetProperty("code").GetString();

    [Fact, Trait("Id", "BC-UC-010-T01"), Trait("Id", "MM-UT-822")]
    public async Task Upload_SmallerJpegOverLarger_GetReturnsExactlyTheNewFile()
    {
        var client = await factory.AdminAsync();
        await CreateAsync(client, "IMG1");

        using var first = await client.PutAsync(ItemRoutes.ImageOf("IMG1"), Form(LargeJpeg));
        using var second = await client.PutAsync(ItemRoutes.ImageOf("IMG1"), Form(SmallJpeg));
        using var get = await factory.NonAdmin().GetAsync(ItemRoutes.ImageOf("IMG1"));

        first.StatusCode.Should().Be(HttpStatusCode.NoContent);
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        get.Content.Headers.ContentType!.MediaType.Should().Be(ItemImage.Jpeg);
        (await get.Content.ReadAsByteArrayAsync()).Should().Equal(SmallJpeg);
    }

    [Fact, Trait("Id", "BC-UC-010-T02"), Trait("Id", "MM-UT-821")]
    public async Task Upload_PngNamedJpg_Returns400MmE821_AndKeepsTheImage()
    {
        var client = await factory.AdminAsync();
        await CreateAsync(client, "IMG2");
        (await client.PutAsync(ItemRoutes.ImageOf("IMG2"), Form(SmallJpeg))).Dispose();

        using var response = await client.PutAsync(ItemRoutes.ImageOf("IMG2"), Form(Png, "photo.jpg", "image/jpeg"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be(ItemImage.InvalidFileCode);
        factory.Items.Images["IMG2"].Should().Equal(SmallJpeg);
    }

    [Fact, Trait("Id", "MM-UT-821")]
    public async Task Upload_OverTenMegabytes_NoFile_OrUnknownItem_Returns400MmE821()
    {
        var client = await factory.AdminAsync();
        await CreateAsync(client, "IMG3");
        var tooLarge = new byte[ItemRoutes.ImageMaxBytes + 1];
        LargeJpeg.CopyTo(tooLarge, 0);

        using var large = await client.PutAsync(ItemRoutes.ImageOf("IMG3"), Form(tooLarge));
        using var noFile = await client.PutAsync(ItemRoutes.ImageOf("IMG3"), new MultipartFormDataContent { { new StringContent("x"), "other" } });
        using var unknown = await client.PutAsync(ItemRoutes.ImageOf("NOPE"), Form(SmallJpeg));

        foreach (var response in new[] { large, noFile, unknown })
        {
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await CodeOf(response)).Should().Be(ItemImage.InvalidFileCode);
        }
        factory.Items.Images.Should().NotContainKey("IMG3");
        ItemRoutes.ImageMaxBytes.Should().Be(ItemImage.MaxBytes);
    }

    [Fact, Trait("Id", "BC-UC-010-T03"), Trait("Id", "MM-UT-823")]
    public async Task Delete_ClearsTheImage_ThenGetReturns404CommonE006()
    {
        var client = await factory.AdminAsync();
        await CreateAsync(client, "IMG4");
        (await client.PutAsync(ItemRoutes.ImageOf("IMG4"), Form(SmallJpeg))).Dispose();

        using var delete = await client.DeleteAsync(ItemRoutes.ImageOf("IMG4"));
        using var get = await client.GetAsync(ItemRoutes.ImageOf("IMG4"));
        using var unknown = await client.DeleteAsync(ItemRoutes.ImageOf("NOPE"));

        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeOf(get)).Should().Be(ItemImageService.NotFoundCode);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, Trait("Id", "BC-UC-010-T03"), Trait("Id", "MM-UT-820")]
    public async Task Get_ItemWithoutImage_ReturnsTheItemGroupImage()
    {
        var client = await factory.AdminAsync();
        await CreateAsync(client, "GRPA");
        var fields = new ItemFields("HEX BOLT", null, null, "INVENTORY", "EA", "PART", "Y", 1m, 1m, null, null, 0m, null, null, null, null, null, null, true, "TAXABLE");
        // Plating G puts GRPAG in item group GRPA (MM-BR-808); the API cannot create such an item
        await factory.Items.InsertAsync(Item.Restore(0, "GRPAG", fields, "G", 1m, 0, 0), "kim", null!, CancellationToken.None);
        (await client.PutAsync(ItemRoutes.ImageOf("GRPA"), Form(LargeJpeg))).Dispose();

        using var get = await factory.NonAdmin().GetAsync(ItemRoutes.ImageOf("GRPAG"));

        get.StatusCode.Should().Be(HttpStatusCode.OK);
        (await get.Content.ReadAsByteArrayAsync()).Should().Equal(LargeJpeg);
    }

    [Fact, Trait("Id", "MM-UT-824")]
    public async Task UploadAndDelete_NonAdmin_Return403()
    {
        var client = await factory.AdminAsync();
        await CreateAsync(client, "IMG5");

        using var upload = await factory.NonAdmin().PutAsync(ItemRoutes.ImageOf("IMG5"), Form(SmallJpeg));
        using var delete = await factory.NonAdmin().DeleteAsync(ItemRoutes.ImageOf("IMG5"));

        upload.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        delete.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        factory.Items.Images.Should().NotContainKey("IMG5");
    }
}
