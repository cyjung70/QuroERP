using QuroErp.Web.Auth;
using QuroErp.Web.Bc.Codes;
using QuroErp.Web.Bc.Companies;
using QuroErp.Web.Bc.Items;
using QuroErp.Web.Bc.Localization;
using QuroErp.Web.Bc.Menus;
using QuroErp.Web.Bc.PurchasePrices;
using QuroErp.Web.Bc.Roles;
using QuroErp.Web.Bc.SetItems;
using QuroErp.Web.Bc.Tax;
using QuroErp.Web.Bc.UserProfiles;
using QuroErp.Web.Bc.Users;
using QuroErp.Web.Components.Screens;
using QuroErp.Web.Mm.GoodsReceipts;
using QuroErp.Web.Mm.Inventory;
using QuroErp.Web.Mm.Lots;
using QuroErp.Web.Mm.PurchaseOrders;
using QuroErp.Web.Mm.Vendors;
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
        services.AddHttpClient<BusinessPlaceApiClient>(http => http.BaseAddress = apiBaseUrl);
        services.AddHttpClient<AuthSettingApiClient>(http => http.BaseAddress = apiBaseUrl);
        services.AddHttpClient<InventorySettingApiClient>(http => http.BaseAddress = apiBaseUrl);
        services.AddHttpClient<CodeApiClient>(http => http.BaseAddress = apiBaseUrl);
        services.AddHttpClient<LocalizationApiClient>(http => http.BaseAddress = apiBaseUrl);
        services.AddHttpClient<ItemApiClient>(http => http.BaseAddress = apiBaseUrl);
        services.AddHttpClient<ItemBarcodeApiClient>(http => http.BaseAddress = apiBaseUrl);
        services.AddHttpClient<SetItemApiClient>(http => http.BaseAddress = apiBaseUrl);
        services.AddHttpClient<WarehouseApiClient>(http => http.BaseAddress = apiBaseUrl);
        services.AddHttpClient<UserProfileApiClient>(http => http.BaseAddress = apiBaseUrl);
        services.AddHttpClient<UserApiClient>(http => http.BaseAddress = apiBaseUrl);
        services.AddHttpClient<RoleApiClient>(http => http.BaseAddress = apiBaseUrl);
        services.AddHttpClient<MenuApiClient>(http => http.BaseAddress = apiBaseUrl);
        services.AddHttpClient<CodeMinorApiClient>(http => http.BaseAddress = apiBaseUrl);
        services.AddHttpClient<CodeMajorApiClient>(http => http.BaseAddress = apiBaseUrl);
        services.AddHttpClient<CurrencyApiClient>(http => http.BaseAddress = apiBaseUrl);
        services.AddHttpClient<TaxCodeApiClient>(http => http.BaseAddress = apiBaseUrl);
        services.AddHttpClient<VendorApiClient>(http => http.BaseAddress = apiBaseUrl);
        services.AddHttpClient<PurchaseOrderApiClient>(http => http.BaseAddress = apiBaseUrl);
        services.AddHttpClient<GoodsReceiptApiClient>(http => http.BaseAddress = apiBaseUrl);
        services.AddHttpClient<LotApiClient>(http => http.BaseAddress = apiBaseUrl);
        services.AddHttpClient<PurchasePriceApiClient>(http => http.BaseAddress = apiBaseUrl);
        services.AddHttpClient<ExchangeRateApiClient>(http => http.BaseAddress = apiBaseUrl);
        services.AddHttpClient<FieldPolicyApiClient>(http => http.BaseAddress = apiBaseUrl);
        // Code lists for combo boxes, read once per circuit
        services.AddScoped<CodeDataSets>();
        // Field policies of a screen, read once per circuit (field-policy.md 3)
        services.AddScoped<FieldPolicyCache>();
        // Company time zone for every shown time (BC-BR-318)
        services.AddScoped(CompanyTime.Create);
        return services;
    }
}
