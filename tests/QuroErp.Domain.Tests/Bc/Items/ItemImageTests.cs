using FluentAssertions;
using QuroErp.Domain.Bc.Items;

namespace QuroErp.Domain.Tests.Bc.Items;

/// <summary>Item image rules (MM-BR-820, MM-BR-821): JPEG by content up to 10 MB; stored images go back with their own type.</summary>
public class ItemImageTests
{
    private static readonly byte[] JpegHeader = [0xFF, 0xD8, 0xFF, 0xE0];
    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Fact, Trait("Id", "MM-UT-821")]
    public void IsValidUpload_JpegUpToMaxBytes_IsAccepted()
    {
        var largest = new byte[ItemImage.MaxBytes];
        JpegHeader.CopyTo(largest, 0);

        ItemImage.IsValidUpload([.. JpegHeader, 1, 2]).Should().BeTrue();
        ItemImage.IsValidUpload(largest).Should().BeTrue();
        ItemImage.MaxBytes.Should().Be(10 * 1024 * 1024);
    }

    [Theory, Trait("Id", "MM-UT-821"), Trait("Id", "BC-UC-010-T02")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01 })] // PNG, whatever its name says
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 })] // GIF
    [InlineData(new byte[] { 0xFF, 0xD8 })] // truncated JPEG signature
    [InlineData(new byte[0])]
    public void IsValidUpload_NotJpeg_IsRejected(byte[] content)
    {
        ItemImage.IsValidUpload(content).Should().BeFalse();
    }

    [Fact, Trait("Id", "MM-UT-821")]
    public void IsValidUpload_OverMaxBytes_IsRejected_AndTheLengthCheckSaysSoBeforeReading()
    {
        var image = new byte[ItemImage.MaxBytes + 1];
        JpegHeader.CopyTo(image, 0);

        ItemImage.IsValidUpload(image).Should().BeFalse();
        ItemImage.IsAllowedLength(ItemImage.MaxBytes + 1L).Should().BeFalse();
        ItemImage.IsAllowedLength(0).Should().BeFalse();
        ItemImage.IsAllowedLength(ItemImage.MaxBytes).Should().BeTrue();
    }

    [Fact, Trait("Id", "MM-UT-820")]
    public void ContentTypeOf_TellsStoredFormats_ElseOctetStream()
    {
        ItemImage.ContentTypeOf([.. JpegHeader, 1]).Should().Be(ItemImage.Jpeg);
        ItemImage.ContentTypeOf([.. PngHeader, 1]).Should().Be("image/png");
        ItemImage.ContentTypeOf("GIF89a"u8).Should().Be("image/gif");
        ItemImage.ContentTypeOf("BM\0\0"u8).Should().Be("image/bmp");
        ItemImage.ContentTypeOf([0x00, 0x01]).Should().Be(ItemImage.Unknown);
    }
}
