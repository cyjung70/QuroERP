using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Application.Bc.Tax;
using QuroErp.Contracts.Bc.Tax;
using QuroErp.Contracts.Common;

namespace QuroErp.Api.Bc.Tax;

/// <summary>
/// Tax code management (BC-UC-028). Any signed-in user or API client can read (FallbackPolicy); changes need Full Access
/// (BC-BR-507, BC-BR-214), checked by the admin role until the menu policy is applied, as on the other screens.
/// </summary>
[ApiController]
public sealed class TaxCodeController(TaxCodeService service) : ControllerBase
{
    /// <summary>Every tax code in code order, with names in lang.</summary>
    /// <param name="lang">Language of Name. When omitted, the request language (Accept-Language) is used.</param>
    /// <param name="useYn">Only codes in use (true) or not in use (false). When omitted, all.</param>
    /// <param name="taxKind">Only codes of this kind. When omitted, all.</param>
    [HttpGet(TaxCodeRoutes.Base)]
    public Task<IReadOnlyList<TaxCodeDto>> Search(
        [FromQuery] string? lang, [FromQuery] bool? useYn, [FromQuery] string? taxKind, CancellationToken cancellationToken) =>
        service.SearchAsync(lang ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, useYn, taxKind, cancellationToken);

    /// <summary>
    /// 200 with every tax code after the save. Nothing is saved when a row fails: 422 BC-E-501 (code), BC-E-502 (kind not of the
    /// company country), BC-E-503 (rate, tax area), BC-E-504 (price included), BC-E-404 (en name) with the tax code or BC-E-405 with the
    /// language; 409 BC-E-505 when NONE is deleted, renamed, given another kind or set not in use, BC-E-506 when documents use a code
    /// whose kind, rate or tax area changes or a deleted code is used by documents or partners, Common-E-004 when a row changed after
    /// it was read.
    /// </summary>
    [HttpPost(TaxCodeRoutes.Batch), Authorize(Roles = "admin")]
    public Task<IReadOnlyList<TaxCodeDto>> Save(
        BatchRequest<TaxCodeSaveRow, TaxCodeKey> request, [FromQuery] string? lang, CancellationToken cancellationToken) =>
        service.SaveAsync(request, lang ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, User.Identity!.Name!, cancellationToken);
}
