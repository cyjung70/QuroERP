namespace QuroErp.Contracts.Bc.Companies;

/// <summary>Company API routes (BC-UC-007). There is only one company, so the routes have no id.</summary>
public static class CompanyRoutes
{
    public const string Base = "api/bc/company";

    /// <summary>GET returns the logo image; PUT uploads it as multipart/form-data field LogoFormField.</summary>
    public const string Logo = Base + "/logo";

    public const string LogoFormField = "file";

    /// <summary>Upload limit (PNG or JPEG up to 1 MB). The API checks it again.</summary>
    public const int LogoMaxBytes = 1024 * 1024;
}
