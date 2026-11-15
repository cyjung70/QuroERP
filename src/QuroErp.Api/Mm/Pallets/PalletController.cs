using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Api.Common.Auth;
using QuroErp.Application.Mm.Pallets;
using QuroErp.Contracts.Mm.Pallets;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Api.Mm.Pallets;

/// <summary>
/// Pallet management (MM-UC-035). Any signed-in user or API client can read (FallbackPolicy), so the PDA can look up a scanned
/// Pallet; split and merge need Full Access on the screen (BC-BR-214), checked by the screen path policy.
/// </summary>
[ApiController]
public sealed class PalletController(PalletService service) : ControllerBase
{
    private const string Screen = ScreenPolicy.Prefix + ScreenPaths.Pallet;

    /// <summary>Pallets in number order, at most 1000 rows.</summary>
    [HttpGet(PalletRoutes.Base)]
    public Task<IReadOnlyList<PalletDto>> Search([FromQuery] PalletSearchQuery query, CancellationToken cancellationToken) =>
        service.SearchAsync(query, cancellationToken);

    /// <summary>The Pallet with its history (MM-BR-916). 404 Common-E-006.</summary>
    [HttpGet(PalletRoutes.ById)]
    public Task<PalletDto> Get(long id, CancellationToken cancellationToken) => service.GetAsync(id, cancellationToken);

    /// <summary>The Pallet of a scanned number. 404 Common-E-006.</summary>
    [HttpGet(PalletRoutes.ByNo)]
    public Task<PalletDto> GetByNo(string palletNo, CancellationToken cancellationToken) => service.GetByNoAsync(palletNo, cancellationToken);

    /// <summary>
    /// 201 with the new Pallet (MM-BR-914). 404 Common-E-006; 409 Common-E-004 when the Pallet changed after it was read;
    /// 422 MM-E-914 when the Pallet is not ACTIVE or the qty is not above 0 and below the Pallet qty.
    /// </summary>
    [HttpPost(PalletRoutes.Split), Authorize(Policy = Screen)]
    public async Task<ActionResult<SplitPalletResultDto>> Split(long id, SplitPalletRequest request, CancellationToken cancellationToken)
    {
        var result = await service.SplitAsync(id, request, User.Identity!.Name!, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = result.PalletId }, result);
    }

    /// <summary>
    /// 200 with the target and its history (MM-BR-915). 404 Common-E-006 when a Pallet does not exist; 422 MM-E-915 when no other
    /// Pallet is named, one is named twice, or one is not ACTIVE or on another item, Lot, warehouse or Location.
    /// </summary>
    [HttpPost(PalletRoutes.Merge), Authorize(Policy = Screen)]
    public Task<PalletDto> Merge(MergePalletsRequest request, CancellationToken cancellationToken) =>
        service.MergeAsync(request, User.Identity!.Name!, cancellationToken);
}
