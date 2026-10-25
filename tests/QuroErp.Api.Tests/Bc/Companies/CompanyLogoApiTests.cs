using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using QuroErp.Application.Bc.Companies;
using QuroErp.Contracts.Bc.Companies;
using QuroErp.Domain.Bc.Companies;

namespace QuroErp.Api.Tests.Bc.Companies;

/// <summary>Company logo API (BC-UC-007, ADR-0024): read by any signed-in caller, upload by admin only.</summary>
public class CompanyLogoApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02];

    [Fact, Trait("Id", "BC-007-IT-10")]
    public async Task Upload_AdminPng_Returns204_ThenGetReturnsImage()
    {
        var client = await factory.AdminAsync();

        using var upload = await client.PutAsync(CompanyRoutes.Logo, Form(Png, "logo.png", "application/octet-stream"));
        using var get = await factory.NonAdmin().GetAsync(CompanyRoutes.Logo);

        upload.StatusCode.Should().Be(HttpStatusCode.NoContent);
        factory.Companies.UpdatedBy.Should().Be(ApiFactory.UserId);
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        get.Content.Headers.ContentType!.MediaType.Should().Be(CompanyLogo.Png);
        (await get.Content.ReadAsByteArrayAsync()).Should().Equal(Png);
    }

    [Fact, Trait("Id", "BC-007-IT-11")]
    public async Task Upload_Gif_Returns422BcE004()
    {
        var client = await factory.AdminAsync();

        using var response = await client.PutAsync(CompanyRoutes.Logo, Form([0x47, 0x49, 0x46, 0x38], "logo.png", "image/png"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be(CompanyLogo.InvalidTypeCode);
    }

    [Fact, Trait("Id", "BC-007-IT-12")]
    public async Task Upload_OverOneMegabyte_Returns422BcE005()
    {
        var client = await factory.AdminAsync();
        var image = new byte[CompanyRoutes.LogoMaxBytes + 1];
        Png.CopyTo(image, 0);

        using var response = await client.PutAsync(CompanyRoutes.Logo, Form(image, "logo.png", "image/png"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be(CompanyLogo.TooLargeCode);
    }

    [Fact, Trait("Id", "BC-007-IT-13")]
    public async Task Upload_NonAdmin_Returns403()
    {
        using var response = await factory.NonAdmin().PutAsync(CompanyRoutes.Logo, Form(Png, "logo.png", "image/png"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, Trait("Id", "BC-007-IT-14")]
    public async Task Get_NoToken_Returns401()
    {
        using var response = await factory.CreateClient().GetAsync(CompanyRoutes.Logo);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, Trait("Id", "BC-007-IT-15")]
    public async Task Get_NoLogo_Returns404BcE007()
    {
        // A fresh host so another test's upload is not visible
        await using var fresh = new ApiFactory();

        using var response = await fresh.NonAdmin().GetAsync(CompanyRoutes.Logo);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be(CompanyLogoService.LogoNotFoundCode);
    }

    private static MultipartFormDataContent Form(byte[] image, string fileName, string contentType)
    {
        var file = new ByteArrayContent(image);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, CompanyRoutes.LogoFormField, fileName } };
    }
}
