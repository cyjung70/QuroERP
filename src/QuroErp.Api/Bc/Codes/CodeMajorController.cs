using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Application.Bc.Codes;
using QuroErp.Contracts.Bc.Codes;

namespace QuroErp.Api.Bc.Codes;

/// <summary>
/// Major code management (BC-UC-023). Any signed-in user or API client can read (FallbackPolicy); changes need Full Access (BC-BR-214),
/// checked by the admin role until the menu policy is applied, as on the other screens.
/// </summary>
[ApiController, Route(CodeMajorRoutes.Base)]
public sealed class CodeMajorController(CodeMajorService service) : ControllerBase
{
    /// <summary>Majors whose code starts with majorCode (every Major when omitted), in code order, with their Minor count.</summary>
    /// <param name="lang">Language of Name. When omitted, the request language (Accept-Language) is used.</param>
    [HttpGet]
    public Task<IReadOnlyList<CodeMajorDto>> Search([FromQuery] string? majorCode, [FromQuery] string? lang, CancellationToken cancellationToken) =>
        service.SearchAsync(majorCode, lang ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, cancellationToken);

    /// <summary>
    /// 200 with every Major after the save. Nothing is saved when a row fails (SD-BR-905): 422 SD-E-901~904 with the row's Major code or
    /// BC-E-405 with the language, 409 Common-E-004 when a row changed after it was read.
    /// </summary>
    [HttpPut, Authorize(Roles = "admin")]
    public Task<IReadOnlyList<CodeMajorDto>> Save(SaveCodeMajorsRequest request, [FromQuery] string? lang, CancellationToken cancellationToken) =>
        service.SaveAsync(request, lang ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, User.Identity!.Name!, cancellationToken);
}
