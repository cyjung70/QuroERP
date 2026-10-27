using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Application.Bc.Localization;
using QuroErp.Contracts.Bc.Localization;

namespace QuroErp.Api.Bc.Localization;

/// <summary>Localization values (BC-UC-027, BC-BR-426). Any signed-in user or API client can read them (FallbackPolicy).</summary>
[ApiController, Route(LocalizationRoutes.Base)]
public sealed class LocalizationController(LocalizationService service) : ControllerBase
{
    /// <param name="lang">Language for currency names. When omitted, the request language (Accept-Language) is used.</param>
    [HttpGet]
    public async Task<LocalizationDto> Get([FromQuery] string? lang, CancellationToken cancellationToken) =>
        await service.GetAsync(lang ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, cancellationToken);
}
