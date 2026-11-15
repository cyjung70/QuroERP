using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Api.Common.Auth;
using QuroErp.Application.Sd.Assemblies;
using QuroErp.Contracts.Sd.Assemblies;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Api.Sd.Assemblies;

/// <summary>
/// Set assembly and disassembly (SD-UC-055). Any signed-in user can read; saves and voids need Full Access on the assembly
/// screen, checked by the screen path policy (BC-BR-214).
/// </summary>
[ApiController]
public sealed class AssemblyController(AssemblyService service) : ControllerBase
{
    private const string Screen = ScreenPolicy.Prefix + ScreenPaths.Assembly;

    /// <summary>Newest first, at most 1000 rows. 422 Common-E-002 for an unknown kind.</summary>
    [HttpGet(AssemblyRoutes.Base), Authorize]
    public Task<IReadOnlyList<AssemblySummaryDto>> Search([FromQuery] AssemblySearchQuery query, CancellationToken cancellationToken) =>
        service.SearchAsync(query, cancellationToken);

    /// <summary>Header and component lines. 404 Common-E-006.</summary>
    [HttpGet(AssemblyRoutes.ById), Authorize]
    public Task<AssemblyDto> Get(long id, CancellationToken cancellationToken) => service.GetAsync(id, cancellationToken);

    /// <summary>
    /// The components of qty Sets and their FIFO Locations and Lots (SD-BR-518); nothing is saved. 422 Common-E-005/002,
    /// SD-E-509.
    /// </summary>
    [HttpGet(AssemblyRoutes.Preview), Authorize]
    public Task<AssemblyPreviewDto> Preview([FromQuery] AssemblyPreviewQuery query, CancellationToken cancellationToken) =>
        service.PreviewAsync(query, cancellationToken);

    /// <summary>
    /// 201: issues the components (BI) and receives the Set (BP) (SD-BR-514, SD-BR-515). 422 SD-E-514, SD-E-509,
    /// Common-E-005/002 and the inventory checks.
    /// </summary>
    [HttpPost(AssemblyRoutes.Base), Authorize(Policy = Screen)]
    public async Task<ActionResult<AssemblyCreatedDto>> Create(SaveAssemblyRequest request, CancellationToken cancellationToken)
    {
        var dto = await service.AssembleAsync(request, User.Identity!.Name!, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    /// <summary>
    /// 201: issues the Set (DA) and gives the components back (DR) (SD-BR-516). 404 Common-E-006; 422 SD-E-516,
    /// Common-E-005/002 and the inventory checks.
    /// </summary>
    [HttpPost(AssemblyRoutes.Disassemble), Authorize(Policy = Screen)]
    public async Task<ActionResult<AssemblyCreatedDto>> Disassemble(long id, DisassembleRequest request, CancellationToken cancellationToken)
    {
        var dto = await service.DisassembleAsync(id, request, User.Identity!.Name!, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    /// <summary>200 with the void row (MM-BR-601). 404 Common-E-006; 409 Common-E-004 when stale, SD-E-516 with a disassembly.</summary>
    [HttpPost(AssemblyRoutes.Void), Authorize(Policy = Screen)]
    public Task<AssemblySummaryDto> Void(long id, VoidAssemblyRequest request, CancellationToken cancellationToken) =>
        service.VoidAsync(id, request, User.Identity!.Name!, cancellationToken);
}
