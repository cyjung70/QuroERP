namespace QuroErp.Domain.Bc.Items;

/// <summary>
/// Item image (bc_item.img) rules MM-BR-820~823. Uploads are JPEG only, decided from the file content, not the name.
/// The server stores and returns the file as sent; display size is up to the screen (MM-BR-820, MM-BR-822).
/// </summary>
public static class ItemImage
{
    /// <summary>Item missing, no file, not JPEG, or larger than MaxBytes. No args.</summary>
    public const string InvalidFileCode = "MM-E-821";

    /// <summary>10 MB (MM-BR-821).</summary>
    public const int MaxBytes = 10 * 1024 * 1024;

    public const string Jpeg = "image/jpeg";

    /// <summary>For migrated images in a format the type cannot be told from.</summary>
    public const string Unknown = "application/octet-stream";

    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];

    // Migrated images were never checked, so reads also recognize these
    private static readonly (byte[] Signature, string ContentType)[] ReadTypes =
    [
        (JpegSignature, Jpeg),
        ([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], "image/png"),
        ("GIF8"u8.ToArray(), "image/gif"),
        ("BM"u8.ToArray(), "image/bmp"),
    ];

    /// <summary>A file of at most MaxBytes may be uploaded; read only files that pass, so a huge upload is not buffered.</summary>
    public static bool IsAllowedLength(long length) => length is > 0 and <= MaxBytes;

    /// <summary>MM-BR-821: one non-empty JPEG file of at most MaxBytes.</summary>
    public static bool IsValidUpload(ReadOnlySpan<byte> content) => IsAllowedLength(content.Length) && content.StartsWith(JpegSignature);

    /// <summary>The MIME type to send a stored image back with.</summary>
    public static string ContentTypeOf(ReadOnlySpan<byte> image)
    {
        foreach (var (signature, contentType) in ReadTypes)
        {
            if (image.StartsWith(signature))
            {
                return contentType;
            }
        }
        return Unknown;
    }
}
