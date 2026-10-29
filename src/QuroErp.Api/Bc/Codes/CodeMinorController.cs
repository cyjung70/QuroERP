using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Application.Bc.Codes;
using QuroErp.Contracts.Bc.Codes;

namespace QuroErp.Api.Bc.Codes;

/// <summary>
/// Minor code management (BC-UC-008). Any signed-in user or API client can read (FallbackPolicy); changes need Full Access (BC-BR-214),
/// checked by the admin role until the menu policy is applied, as on the other screens.
/// </summary>
[ApiController, Route(CodeMinorRoutes.Base)]
public sealed class CodeMinorController(CodeMinorService service) : ControllerBase
{
    /// <summary>Every Minor code of majorCode in sort order (SD-BR-910). 400 Common-E-005 without a Major, 404 Common-E-003 for an unknown one.</summary>
    /// <param name="lang">Language of Name. When omitted, the request language (Accept-Language) is used.</param>
    [HttpGet]
    public Task<IReadOnlyList<CodeMinorDto>> Search([FromQuery] string? majorCode, [FromQuery] string? lang, CancellationToken cancellationToken) =>
        service.SearchAsync(majorCode, lang ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, cancellationToken);

    /// <summary>
    /// 200 with every Minor code of the Major after the save. Nothing is saved when a row fails (SD-BR-905): 422 SD-E-911~914, SD-E-916
    /// with the row's Minor code or BC-E-405 with the language, 409 Common-E-004 when a row changed after it was read.
    /// </summary>
    [HttpPut, Authorize(Roles = "admin")]
    public Task<IReadOnlyList<CodeMinorDto>> Save(SaveCodeMinorsRequest request, [FromQuery] string? lang, CancellationToken cancellationToken) =>
        service.SaveAsync(request, lang ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, User.Identity!.Name!, cancellationToken);
}
