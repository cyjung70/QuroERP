using QuroErp.Application.Common;
using QuroErp.Application.Common.Persistence;
using QuroErp.Domain.Bc.Items;

namespace QuroErp.Application.Bc.Items;

/// <summary>Image bytes with the MIME type to send back.</summary>
public sealed record ItemImageFile(byte[] Content, string ContentType);

/// <summary>Item image view and upload (BC-UC-010, MM-BR-820~824).</summary>
public sealed class ItemImageService(IItemRepository items, IUnitOfWorkFactory unitOfWork)
{
    /// <summary>The item or its image does not exist; the screen then shows the default image. Arg: item code.</summary>
    public const string NotFoundCode = "Common-E-006";

    /// <summary>The item's image, else its item group's (MM-BR-820).</summary>
    /// <exception cref="NotFoundException">Common-E-006 when the item does not exist or no image is found.</exception>
    public async Task<ItemImageFile> GetAsync(string itemCd, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var image = await items.FindImageAsync(itemCd, uow.Transaction, cancellationToken)
            ?? throw new NotFoundException(NotFoundCode, itemCd);
        await uow.CommitAsync(cancellationToken);
        return new ItemImageFile(image, ItemImage.ContentTypeOf(image));
    }

    /// <summary>Replaces the whole item image with the file as sent (MM-BR-821, MM-BR-822).</summary>
    /// <param name="content">The file, or null when none was sent.</param>
    /// <param name="length">The file size; checked before the file is read.</param>
    /// <exception cref="BadRequestException">MM-E-821 when there is no file, it is not JPEG or too large, or the item does not exist.</exception>
    public async Task UploadAsync(string itemCd, Stream? content, long length, CancellationToken cancellationToken)
    {
        if (content is null || !ItemImage.IsAllowedLength(length))
        {
            throw new BadRequestException(ItemImage.InvalidFileCode);
        }
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var image = buffer.ToArray();
        if (!ItemImage.IsValidUpload(image))
        {
            throw new BadRequestException(ItemImage.InvalidFileCode);
        }

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        if (!await items.SaveImageAsync(itemCd, image, uow.Transaction, cancellationToken))
        {
            throw new BadRequestException(ItemImage.InvalidFileCode);
        }
        await uow.CommitAsync(cancellationToken);
    }

    /// <summary>Clears the item image; reads then fall back to the item group image (MM-BR-823).</summary>
    /// <exception cref="NotFoundException">Common-E-006 when the item does not exist.</exception>
    public async Task DeleteAsync(string itemCd, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        if (!await items.SaveImageAsync(itemCd, null, uow.Transaction, cancellationToken))
        {
            throw new NotFoundException(NotFoundCode, itemCd);
        }
        await uow.CommitAsync(cancellationToken);
    }
}
