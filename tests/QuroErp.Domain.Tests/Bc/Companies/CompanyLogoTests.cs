using FluentAssertions;
using QuroErp.Domain.Bc.Companies;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Tests.Bc.Companies;

/// <summary>Company logo validation (ADR-0024): PNG or JPEG by signature, up to 1 MB.</summary>
public class CompanyLogoTests
{
    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegHeader = [0xFF, 0xD8, 0xFF, 0xE0];

    [Fact, Trait("Id", "BC-007-T01")]
    public void Create_Png_DetectsPngType()
    {
        var logo = CompanyLogo.Create([.. PngHeader, 1, 2, 3]);

        logo.ContentType.Should().Be(CompanyLogo.Png);
        logo.Image.Should().HaveCount(11);
    }

    [Fact, Trait("Id", "BC-007-T02")]
    public void Create_Jpeg_DetectsJpegType()
    {
        CompanyLogo.Create([.. JpegHeader, 1]).ContentType.Should().Be(CompanyLogo.Jpeg);
    }

    [Fact, Trait("Id", "BC-007-T03")]
    public void Create_ExactlyMaxBytes_IsAccepted()
    {
        var image = new byte[CompanyLogo.MaxBytes];
        PngHeader.CopyTo(image, 0);

        CompanyLogo.Create(image).ContentType.Should().Be(CompanyLogo.Png);
    }

    [Fact, Trait("Id", "BC-007-T04")]
    public void Create_OverMaxBytes_ThrowsBcE005WithKbLimit()
    {
        var image = new byte[CompanyLogo.MaxBytes + 1];
        PngHeader.CopyTo(image, 0);

        var e = FluentActions.Invoking(() => CompanyLogo.Create(image)).Should().Throw<DomainException>().Which;

        e.Code.Should().Be(CompanyLogo.TooLargeCode);
        e.Args.Should().Equal(1024);
    }

    [Theory, Trait("Id", "BC-007-T05")]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 })] // GIF
    [InlineData(new byte[] { 0x3C, 0x73, 0x76, 0x67 })] // <svg
    [InlineData(new byte[] { 0x89, 0x50, 0x4E })] // truncated PNG signature
    [InlineData(new byte[0])]
    public void Create_NotPngOrJpeg_ThrowsBcE004(byte[] image)
    {
        FluentActions.Invoking(() => CompanyLogo.Create(image))
            .Should().Throw<DomainException>().Which.Code.Should().Be(CompanyLogo.InvalidTypeCode);
    }
}
