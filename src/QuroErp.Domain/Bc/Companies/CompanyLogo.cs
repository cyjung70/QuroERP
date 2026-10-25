using QuroErp.Domain.Common;

namespace QuroErp.Domain.Bc.Companies;

/// <summary>
/// Company logo for printed documents (bc_company.logo_image, logo_content_type; ADR-0024).
/// Only PNG and JPEG up to MaxBytes. The type is decided from the file signature, not from what the client says.
/// </summary>
public sealed class CompanyLogo
{
    public const string Png = "image/png";
    public const string Jpeg = "image/jpeg";

    /// <summary>1 MB. Enough for a document header logo.</summary>
    public const int MaxBytes = 1024 * 1024;

    /// <summary>The file is not PNG or JPEG.</summary>
    public const string InvalidTypeCode = "BC-E-004";

    /// <summary>{0} = maximum size in KB.</summary>
    public const string TooLargeCode = "BC-E-005";

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];

    private CompanyLogo(byte[] image, string contentType)
    {
        Image = image;
        ContentType = contentType;
    }

    public byte[] Image { get; }

    public string ContentType { get; }

    /// <exception cref="DomainException">BC-E-005 when larger than MaxBytes, BC-E-004 when not PNG or JPEG.</exception>
    public static CompanyLogo Create(byte[] image)
    {
        if (image.Length > MaxBytes)
        {
            throw new DomainException(TooLargeCode, MaxBytes / 1024);
        }
        var contentType = DetectContentType(image) ?? throw new DomainException(InvalidTypeCode);
        return new CompanyLogo(image, contentType);
    }

    private static string? DetectContentType(ReadOnlySpan<byte> image) =>
        image.StartsWith(PngSignature) ? Png
        : image.StartsWith(JpegSignature) ? Jpeg
        : null;
}
