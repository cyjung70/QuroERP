using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Application.Bc.Localization;
using QuroErp.Contracts.Bc.Localization;
using QuroErp.Contracts.Common;

namespace QuroErp.Api.Bc.Localization;

/// <summary>
/// Exchange rate management (BC-UC-024). Any signed-in user or API client can read (FallbackPolicy); changes need Full Access (BC-BR-214),
/// checked by the admin role until the menu policy is applied, as on the other screens.
/// </summary>
[ApiController]
public sealed class ExchangeRateController(ExchangeRateService service) : ControllerBase
{
    /// <summary>Rates by date (newest first), then currency (SD-BR-908).</summary>
    /// <param name="from">First date (yyyy-MM-dd). When omitted, one year before <paramref name="to"/>.</param>
    /// <param name="to">Last date. When omitted, today in the company time zone.</param>
    /// <param name="currencyCode">One currency. When omitted, all.</param>
    [HttpGet(ExchangeRateRoutes.Base)]
    public Task<IReadOnlyList<ExchangeRateDto>> Search(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? currencyCode, CancellationToken cancellationToken) =>
        service.SearchAsync(from, to, currencyCode, cancellationToken);

    /// <summary>
    /// 200 with the rates of the period and currency after the save. Nothing is saved when a row fails (SD-BR-905): 422 SD-E-907
    /// (missing date, currency not in use or the base currency, rate missing, not above 0 or over 10 decimals) or SD-E-906 (the date
    /// and currency already have a rate), both with the date and currency; 409 Common-E-004 when a row changed after it was read.
    /// </summary>
    [HttpPost(ExchangeRateRoutes.Batch), Authorize(Roles = "admin")]
    public Task<IReadOnlyList<ExchangeRateDto>> Save(
        BatchRequest<ExchangeRateSaveRow, ExchangeRateKey> request, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] string? currencyCode, CancellationToken cancellationToken) =>
        service.SaveAsync(request, from, to, currencyCode, User.Identity!.Name!, cancellationToken);
}
