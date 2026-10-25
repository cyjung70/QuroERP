using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Application.Common.Codes;
using QuroErp.Contracts.Common.Codes;

namespace QuroErp.Api.Common.Codes;

/// <summary>Code lists (codes.md "화면"). Any signed-in user or API client can read them (FallbackPolicy).</summary>
[ApiController, Route(CodeRoutes.Base)]
public sealed class CodesController(CodeService service) : ControllerBase
{
    /// <summary>Active codes of majorCode in sort_order with translated names.</summary>
    /// <param name="lang">ko or en. When omitted, the request language (Accept-Language) is used, then ko.
    /// Unsupported values fall back to ko (normalized in Application).</param>
    [HttpGet("{majorCode}")]
    public async Task<IReadOnlyList<CodeItemDto>> Get(string majorCode, [FromQuery] string? lang, CancellationToken cancellationToken) =>
        await service.GetCodesAsync(majorCode, lang ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, cancellationToken);
}
