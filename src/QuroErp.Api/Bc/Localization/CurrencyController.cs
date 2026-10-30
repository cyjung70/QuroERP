using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Application.Bc.Localization;
using QuroErp.Contracts.Bc.Localization;
using QuroErp.Contracts.Common;

namespace QuroErp.Api.Bc.Localization;

/// <summary>
/// Currency management (BC-UC-026). Any signed-in user or API client can read (FallbackPolicy); changes need Full Access (BC-BR-214),
/// checked by the admin role until the menu policy is applied, as on the other screens.
/// </summary>
[ApiController]
public sealed class CurrencyController(CurrencyService service) : ControllerBase
{
    /// <summary>Every currency in code order, with names in lang.</summary>
    /// <param name="lang">Language of Name. When omitted, the request language (Accept-Language) is used.</param>
    /// <param name="useYn">Only currencies in use (true) or not in use (false). When omitted, all.</param>
    [HttpGet(CurrencyRoutes.Base)]
    public Task<IReadOnlyList<CurrencyMasterDto>> Search([FromQuery] string? lang, [FromQuery] bool? useYn, CancellationToken cancellationToken) =>
        service.SearchAsync(lang ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, useYn, cancellationToken);

    /// <summary>
    /// 200 with every currency after the save. Nothing is saved when a row fails: 422 BC-E-411 (code, decimals), BC-E-428 (country),
    /// BC-E-404 (en name), BC-E-312 (base currency set not in use) with the currency code or BC-E-405 with the language;
    /// 409 BC-E-411 or BC-E-428 when documents use the currency whose decimals or country change, Common-E-004 when a row changed
    /// after it was read.
    /// </summary>
    [HttpPost(CurrencyRoutes.Batch), Authorize(Roles = "admin")]
    public Task<IReadOnlyList<CurrencyMasterDto>> Save(
        BatchRequest<CurrencySaveRow, CurrencyKey> request, [FromQuery] string? lang, CancellationToken cancellationToken) =>
        service.SaveAsync(request, lang ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, User.Identity!.Name!, cancellationToken);
}
