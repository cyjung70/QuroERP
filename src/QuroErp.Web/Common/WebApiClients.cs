using QuroErp.Web.Auth;
using QuroErp.Web.Bc.Codes;
using QuroErp.Web.Bc.Companies;
using QuroErp.Web.Bc.Items;
using QuroErp.Web.Bc.Localization;
using QuroErp.Web.Bc.Menus;
using QuroErp.Web.Bc.Roles;
using QuroErp.Web.Bc.UserProfiles;
using QuroErp.Web.Bc.Users;
using QuroErp.Web.Mm.Inventory;
using QuroErp.Web.Mm.Warehouses;

namespace QuroErp.Web.Common;

/// <summary>Typed clients for business APIs. They send the signed-in user's token (AccessTokenProvider).</summary>
public static class WebApiClients
{
    public static IServiceCollection AddWebApiClients(this IServiceCollection services, IConfiguration configuration)
    {
        var apiBaseUrl = new Uri(configuration[WebAuthentication.ApiBaseUrlKey]
            ?? throw new InvalidOperationException($"Setting {WebAuthentication.ApiBaseUrlKey} is missing."));

        services.AddHttpClient<CompanyApiClient>(http => http.BaseAddress = apiBaseUrl);
        services.AddHttpClient<AuthSettingApiClient>(http => http.BaseAddress = apiBaseUrl);
        services.AddHttpClient<InventorySettingApiClient>(http => http.BaseAddress = apiBaseUrl);
        services.AddHttpClient<CodeApiClient>(http => http.BaseAddress = apiBaseUrl);
        services.AddHttpClient<LocalizationApiClient>(http => http.BaseAddress = apiBaseUrl);
        services.AddHttpClient<ItemApiClient>(http => http.BaseAddress = apiBaseUrl);
        services.AddHttpClient<WarehouseApiClient>(http => http.BaseAddress = apiBaseUrl);
        services.AddHttpClient<UserProfileApiClient>(http => http.BaseAddress = apiBaseUrl);
        services.AddHttpClient<UserApiClient>(http => http.BaseAddress = apiBaseUrl);
        services.AddHttpClient<RoleApiClient>(http => http.BaseAddress = apiBaseUrl);
        services.AddHttpClient<MenuApiClient>(http => http.BaseAddress = apiBaseUrl);
        services.AddHttpClient<CodeMinorApiClient>(http => http.BaseAddress = apiBaseUrl);
        services.AddHttpClient<CodeMajorApiClient>(http => http.BaseAddress = apiBaseUrl);
        services.AddHttpClient<CurrencyApiClient>(http => http.BaseAddress = apiBaseUrl);
        return services;
    }
}
