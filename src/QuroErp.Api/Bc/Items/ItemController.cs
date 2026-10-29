using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Application.Bc.Items;
using QuroErp.Contracts.Bc.Items;

namespace QuroErp.Api.Bc.Items;

/// <summary>
/// The item master screen (BC-UC-009). Any signed-in user can read; changes need Full Access (BC-BR-214),
/// checked by the admin role until the menu policy (security.md "권한") exists, as on the company screen.
/// </summary>
[ApiController, Route(ItemRoutes.Base)]
public sealed class ItemController(ItemService service, ItemImageService images) : ControllerBase
{
    /// <summary>200 with the item image, else its item group's (MM-BR-820). 404 Common-E-006 when there is none.</summary>
    [HttpGet("{itemCd}/" + ItemRoutes.Image)]
    public async Task<IActionResult> GetImage(string itemCd, CancellationToken cancellationToken)
    {
        var image = await images.GetAsync(itemCd, cancellationToken);
        return File(image.Content, image.ContentType);
    }

    /// <summary>
    /// 204. multipart/form-data with field "file": one JPEG up to 10 MB, replacing the whole image (MM-BR-821, MM-BR-822).
    /// 400 MM-E-821. Larger requests are cut off by the size limit.
    /// </summary>
    [HttpPut("{itemCd}/" + ItemRoutes.Image), Authorize(Roles = "admin")]
    [RequestSizeLimit(ItemRoutes.ImageMaxBytes + 64 * 1024)]
    public async Task<IActionResult> UploadImage(
        string itemCd, [FromForm(Name = ItemRoutes.ImageFormField)] IFormFile? file, CancellationToken cancellationToken)
    {
        await using var content = file?.OpenReadStream();
        await images.UploadAsync(itemCd, content, file?.Length ?? 0, cancellationToken);
        return NoContent();
    }

    /// <summary>204. The screen asks for confirmation first (MM-BR-823). 404 Common-E-006 when the item does not exist.</summary>
    [HttpDelete("{itemCd}/" + ItemRoutes.Image), Authorize(Roles = "admin")]
    public async Task<IActionResult> DeleteImage(string itemCd, CancellationToken cancellationToken)
    {
        await images.DeleteAsync(itemCd, cancellationToken);
        return NoContent();
    }

    [HttpGet]
    public Task<ItemListDto> Search([FromQuery] ItemSearchQuery query, CancellationToken cancellationToken) =>
        service.SearchAsync(query, cancellationToken);

    [HttpGet("{id:long}")]
    public Task<ItemDto> Get(long id, CancellationToken cancellationToken) => service.GetAsync(id, cancellationToken);

    /// <summary>200 with the previous item in item class, item code order; 204 at the first item.</summary>
    [HttpGet("{id:long}/" + ItemRoutes.Previous)]
    public async Task<ActionResult<ItemDto>> Previous(long id, CancellationToken cancellationToken) =>
        await service.GetAdjacentAsync(id, forward: false, cancellationToken) is { } item ? item : NoContent();

    /// <summary>200 with the next item in item class, item code order; 204 at the last item.</summary>
    [HttpGet("{id:long}/" + ItemRoutes.Next)]
    public async Task<ActionResult<ItemDto>> Next(long id, CancellationToken cancellationToken) =>
        await service.GetAdjacentAsync(id, forward: true, cancellationToken) is { } item ? item : NoContent();

    /// <summary>201 with the saved item. 422 MM-E-801~831 or Common-E-002.</summary>
    [HttpPost, Authorize(Roles = "admin")]
    public async Task<ActionResult<ItemDto>> Create(CreateItemRequest request, CancellationToken cancellationToken)
    {
        var dto = await service.CreateAsync(request, User.Identity!.Name!, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    /// <summary>200 with the saved item. 409 Common-E-004 when updatedAt is stale.</summary>
    [HttpPut("{id:long}"), Authorize(Roles = "admin")]
    public Task<ItemDto> Update(long id, UpdateItemRequest request, CancellationToken cancellationToken) =>
        service.UpdateAsync(id, request, User.Identity!.Name!, cancellationToken);

    /// <summary>204. 422 MM-E-807 when the item is used; 409 Common-E-004 when updatedAt is stale.</summary>
    [HttpDelete("{id:long}"), Authorize(Roles = "admin")]
    public async Task<IActionResult> Delete(long id, [FromQuery] DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, updatedAt, cancellationToken);
        return NoContent();
    }
}
