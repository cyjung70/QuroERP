namespace QuroErp.Application.Common.Auth;

/// <summary>Where an auth request came from. Stored in the login log.</summary>
/// <param name="IpAddr">Browser IP (X-Forwarded-For from Web).</param>
/// <param name="UserAgent">Browser User-Agent, cut to <see cref="MaxUserAgentLength"/>.</param>
public sealed record AuthClient(string? IpAddr, string? UserAgent)
{
    public const int MaxUserAgentLength = 300;

    public static AuthClient Create(string? ipAddr, string? userAgent) =>
        new(ipAddr, userAgent is { Length: > MaxUserAgentLength } ? userAgent[..MaxUserAgentLength] : userAgent);
}
