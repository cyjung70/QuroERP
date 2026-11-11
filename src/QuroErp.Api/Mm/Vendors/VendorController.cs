using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Api.Common.Auth;
using QuroErp.Application.Mm.Vendors;
using QuroErp.Contracts.Mm.Vendors;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Api.Mm.Vendors;

/// <summary>
/// The vendor master screen (MM-UC-001). Any signed-in user can read; changes need Full Access on the screen,
/// checked by the screen path policy (BC-BR-214).
/// </summary>
[ApiController, Route(VendorRoutes.Base)]
public sealed class VendorController(VendorService service) : ControllerBase
{
    private const string Screen = ScreenPolicy.Prefix + ScreenPaths.Vendor;

    [HttpGet]
    public Task<VendorListDto> Search([FromQuery] VendorSearchQuery query, CancellationToken cancellationToken) =>
        service.SearchAsync(query, cancellationToken);

    [HttpGet("{id:long}")]
    public Task<VendorDto> Get(long id, CancellationToken cancellationToken) => service.GetAsync(id, cancellationToken);

    /// <summary>200 with the previous vendor in name order; 204 at the first vendor.</summary>
    [HttpGet("{id:long}/" + VendorRoutes.Previous)]
    public async Task<ActionResult<VendorDto>> Previous(long id, CancellationToken cancellationToken) =>
        await service.GetAdjacentAsync(id, forward: false, cancellationToken) is { } vendor ? vendor : NoContent();

    /// <summary>200 with the next vendor in name order; 204 at the last vendor.</summary>
    [HttpGet("{id:long}/" + VendorRoutes.Next)]
    public async Task<ActionResult<VendorDto>> Next(long id, CancellationToken cancellationToken) =>
        await service.GetAdjacentAsync(id, forward: true, cancellationToken) is { } vendor ? vendor : NoContent();

    /// <summary>201 with the saved vendor and its warnings. 422 MM-E-851~872, BC-E-415, BC-E-521~523 or Common-E-002.</summary>
    [HttpPost, Authorize(Policy = Screen)]
    public async Task<ActionResult<VendorDto>> Create(CreateVendorRequest request, CancellationToken cancellationToken)
    {
        var dto = await service.CreateAsync(request, User.Identity!.Name!, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    /// <summary>200 with the saved vendor and its warnings. 409 Common-E-004 when updatedAt is stale.</summary>
    [HttpPut("{id:long}"), Authorize(Policy = Screen)]
    public Task<VendorDto> Update(long id, UpdateVendorRequest request, CancellationToken cancellationToken) =>
        service.UpdateAsync(id, request, User.Identity!.Name!, cancellationToken);

    /// <summary>204. 422 MM-E-857 when the vendor is used; 409 Common-E-004 when updatedAt is stale.</summary>
    [HttpDelete("{id:long}"), Authorize(Policy = Screen)]
    public async Task<IActionResult> Delete(long id, [FromQuery] DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, updatedAt, cancellationToken);
        return NoContent();
    }
}
