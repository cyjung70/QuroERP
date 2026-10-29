using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Application.Bc.Companies;
using QuroErp.Contracts.Bc.Companies;

namespace QuroErp.Api.Bc.Companies;

/// <summary>
/// The company (BC-UC-007, docs/architecture/print-documents.md). Any signed-in user can read it and its logo. Changes need Full Access on the company menu
/// (BC-BR-305), checked by the admin role until the menu policy is applied. There is no POST or DELETE of the company (BC-BR-301, 405).
/// </summary>
[ApiController]
public sealed class CompanyController(CompanyService companies, CompanyLogoService logos) : ControllerBase
{
    [HttpGet(CompanyRoutes.Base)]
    public Task<CompanyDto> Get(CancellationToken cancellationToken) => companies.GetAsync(cancellationToken);

    /// <summary>
    /// 200 with the saved company. 400 BC-E-313 or an unknown property (BC-BR-307), 422 BC-E-302~316, 409 Common-E-004 or
    /// BC-E-312/313/316 when trading data blocks the change.
    /// </summary>
    [HttpPut(CompanyRoutes.Base), Authorize(Roles = "admin")]
    public Task<CompanyDto> Update(UpdateCompanyRequest request, CancellationToken cancellationToken) =>
        companies.UpdateAsync(request, User.Identity!.Name!, cancellationToken);

    [HttpGet(CompanyRoutes.Logo)]
    public async Task<IActionResult> GetLogo(CancellationToken cancellationToken)
    {
        var logo = await logos.GetLogoAsync(cancellationToken);
        return File(logo.Content, logo.ContentType);
    }

    /// <summary>multipart/form-data with field "file". PNG or JPEG up to 1 MB.</summary>
    [HttpPut(CompanyRoutes.Logo), Authorize(Roles = "admin")]
    [RequestSizeLimit(CompanyRoutes.LogoMaxBytes + 64 * 1024)]
    public async Task<IActionResult> UploadLogo([FromForm(Name = CompanyRoutes.LogoFormField)] IFormFile file, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);
        await logos.UploadLogoAsync(buffer.ToArray(), User.Identity!.Name!, cancellationToken);
        return NoContent();
    }

    [HttpDelete(CompanyRoutes.Logo), Authorize(Roles = "admin")]
    public async Task<IActionResult> DeleteLogo(CancellationToken cancellationToken)
    {
        await logos.DeleteLogoAsync(User.Identity!.Name!, cancellationToken);
        return NoContent();
    }
}
