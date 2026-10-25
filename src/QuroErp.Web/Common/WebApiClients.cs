using QuroErp.Web.Auth;
using QuroErp.Web.Bc.Companies;
using QuroErp.Web.Mm.Inventory;

namespace QuroErp.Web.Common;

/// <summary>Typed clients for business APIs. They send the signed-in user's token (AccessTokenProvider).</summary>
public static class WebApiClients
{
    public static IServiceCollection AddWebApiClients(this IServiceCollection services, IConfiguration configuration)
    {
        var apiBaseUrl = new Uri(configuration[WebAuthentication.ApiBaseUrlKey]
            ?? throw new InvalidOperationException($"Setting {WebAuthentication.ApiBaseUrlKey} is missing."));

        services.AddHttpClient<CompanyApiClient>(http => http.BaseAddress = apiBaseUrl);
        services.AddHttpClient<InventorySettingApiClient>(http => http.BaseAddress = apiBaseUrl);
        return services;
    }
}
