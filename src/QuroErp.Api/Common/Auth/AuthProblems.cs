using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using QuroErp.Application.Common.Auth;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Api.Common.Auth;

/// <summary>
/// Writes AUTH-xxx errors as Problem Details { type, title, status, code, detail, lockedUntil } with title and detail
/// in the request language (login.md "보안·감사").
/// </summary>
public static class AuthProblems
{
    private static readonly string[] Languages = ["ko", "en"];

    /// <summary>HTTP status for each code (login.md, BC login spec "REST API").</summary>
    public static int Status(string code) => code switch
    {
        AuthCodes.Required or AuthCodes.PasswordLength or AuthCodes.PasswordCategories or AuthCodes.PasswordReused
            or AuthCodes.PasswordSameAsUserId or AuthCodes.CurrentPasswordIncorrect => StatusCodes.Status400BadRequest,
        AuthCodes.Locked => StatusCodes.Status423Locked,
        AuthCodes.Inactive or AuthCodes.PasswordChangeRequired => StatusCodes.Status403Forbidden,
        AuthCodes.TooManyRequests => StatusCodes.Status429TooManyRequests,
        _ => StatusCodes.Status401Unauthorized,
    };

    /// <summary>Uses the language from the login request body for the messages of this request.</summary>
    public static void UseLanguage(HttpContext httpContext, string? language)
    {
        if (language is not null && Languages.Contains(language, StringComparer.OrdinalIgnoreCase))
        {
            var culture = new RequestCulture(language.ToLowerInvariant());
            httpContext.Features.Set<IRequestCultureFeature>(new RequestCultureFeature(culture, null));
        }
    }

    public static ValueTask<bool> WriteAsync(HttpContext httpContext, AuthException exception)
    {
        var clock = httpContext.RequestServices.GetRequiredService<TimeProvider>();
        var args = exception.Code switch
        {
            AuthCodes.Required => exception.Args.Select(a => (object)Localize(httpContext, $"Auth.Field.{a}")).ToArray(),
            // The unlock time is shown in the server time zone [추정: login.md says the user's time zone, which is not stored]
            AuthCodes.Locked when exception.LockedUntil is { } until =>
                [TimeZoneInfo.ConvertTime(until, clock.LocalTimeZone).ToString("HH:mm", CultureInfo.InvariantCulture)],
            _ => exception.Args.ToArray(),
        };

        var extensions = new Dictionary<string, object?>();
        if (exception.LockedUntil is not null)
        {
            extensions["lockedUntil"] = exception.LockedUntil;
        }
        if (exception.RemainingAttempts is not null)
        {
            extensions["remainingAttempts"] = exception.RemainingAttempts;
        }
        return WriteAsync(httpContext, exception.Code, args, extensions);
    }

    public static ValueTask<bool> WriteAsync(
        HttpContext httpContext, string code, object[]? args = null, IDictionary<string, object?>? extensions = null)
    {
        var status = Status(code);
        var message = Localize(httpContext, code, args ?? []);
        var problem = new ProblemDetails { Status = status, Title = message, Detail = message, Extensions = { ["code"] = code, ["args"] = args ?? [] } };
        foreach (var (key, value) in extensions ?? new Dictionary<string, object?>())
        {
            problem.Extensions[key] = value;
        }

        httpContext.Response.StatusCode = status;
        return httpContext.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(
            new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem });
    }

    private static string Localize(HttpContext httpContext, string key, params object[] args)
    {
        // The exception handler and the rate limiter run outside the action, so the culture is set here from the request
        var previous = CultureInfo.CurrentUICulture;
        var culture = httpContext.Features.Get<IRequestCultureFeature>()?.RequestCulture.UICulture;
        try
        {
            if (culture is not null)
            {
                CultureInfo.CurrentUICulture = culture;
            }
            return httpContext.RequestServices.GetRequiredService<IStringLocalizer<ApiResource>>()[key, args].Value;
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
