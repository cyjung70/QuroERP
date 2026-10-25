namespace QuroErp.Web.Common;

public static class LocalUrl
{
    /// <summary>다른 사이트로 보내지 않도록 같은 사이트 경로만 허용한다. 아니면 "/".</summary>
    public static string OrRoot(string? url) =>
        url is not null && url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal)
            && !url.StartsWith("/\\", StringComparison.Ordinal)
            ? url
            : "/";
}
