using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Application.Bc.Companies;
using QuroErp.Contracts.Bc.Companies;

namespace QuroErp.Api.Bc.Companies;

/// <summary>Company logo (BC-UC-007, ADR-0024). Any signed-in user can read it; only admin can upload.</summary>
[ApiController]
public sealed class CompanyController(CompanyLogoService service) : ControllerBase
{
    [HttpGet(CompanyRoutes.Logo)]
    public async Task<IActionResult> GetLogo(CancellationToken cancellationToken)
    {
        var logo = await service.GetLogoAsync(cancellationToken);
        return File(logo.Content, logo.ContentType);
    }

    /// <summary>multipart/form-data with field "file". PNG or JPEG up to 1 MB.</summary>
    [HttpPut(CompanyRoutes.Logo), Authorize(Roles = "admin")]
    [RequestSizeLimit(CompanyRoutes.LogoMaxBytes + 64 * 1024)]
    public async Task<IActionResult> UploadLogo([FromForm(Name = CompanyRoutes.LogoFormField)] IFormFile file, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);
        await service.UploadLogoAsync(buffer.ToArray(), User.Identity!.Name!, cancellationToken);
        return NoContent();
    }
}
