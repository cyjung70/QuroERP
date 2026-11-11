using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Api.Common.Auth;
using QuroErp.Application.Bc.Tax;
using QuroErp.Contracts.Bc.Tax;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Api.Bc.Tax;

/// <summary>
/// US tax areas (BC-UC-029). Any signed-in user can read; changes need Full Access on the screen (BC-BR-507, BC-BR-214),
/// checked by the screen path policy. Writes answer 409 Common-E-004 when updatedAt is stale.
/// </summary>
[ApiController, Route(TaxAreaRoutes.Base)]
public sealed class TaxAreaController(TaxAreaService service) : ControllerBase
{
    private const string Screen = ScreenPolicy.Prefix + ScreenPaths.TaxArea;

    /// <summary>Areas in code order with the rate valid today.</summary>
    [HttpGet]
    public Task<IReadOnlyList<TaxAreaSummaryDto>> Search([FromQuery] TaxAreaSearchQuery query, CancellationToken cancellationToken) =>
        service.SearchAsync(query, cancellationToken);

    /// <summary>Header and rate lines. 404 Common-E-006.</summary>
    [HttpGet("{id:long}")]
    public Task<TaxAreaDto> Get(long id, CancellationToken cancellationToken) => service.GetAsync(id, cancellationToken);

    /// <summary>201 with the new area. 422 BC-E-511, BC-E-512, Common-E-002 (state).</summary>
    [HttpPost, Authorize(Policy = Screen)]
    public async Task<ActionResult<TaxAreaDto>> Create(SaveTaxAreaRequest request, CancellationToken cancellationToken)
    {
        var dto = await service.CreateAsync(request, User.Identity!.Name!, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    /// <summary>200 with the saved area; the sent lines replace the stored ones. 422 as for POST.</summary>
    [HttpPut("{id:long}"), Authorize(Policy = Screen)]
    public Task<TaxAreaDto> Update(long id, SaveTaxAreaRequest request, CancellationToken cancellationToken) =>
        service.UpdateAsync(id, request, User.Identity!.Name!, cancellationToken);

    /// <summary>204. 409 BC-E-511 when a tax code uses the area.</summary>
    [HttpDelete("{id:long}"), Authorize(Policy = Screen)]
    public async Task<IActionResult> Delete(long id, [FromQuery] DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, updatedAt, cancellationToken);
        return NoContent();
    }
}
