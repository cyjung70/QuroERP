using Microsoft.Extensions.DependencyInjection;
using QuroErp.Application.Bc.BusinessPlaces;
using QuroErp.Application.Bc.Codes;
using QuroErp.Application.Bc.Companies;
using QuroErp.Application.Bc.FieldPolicies;
using QuroErp.Application.Bc.Items;
using QuroErp.Application.Common;
using QuroErp.Application.Bc.PurchasePrices;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Bc.Tax;
using QuroErp.Application.Bc.Menus;
using QuroErp.Application.Bc.Roles;
using QuroErp.Application.Bc.UserProfiles;
using QuroErp.Application.Bc.Users;
using QuroErp.Application.Common.Auth;
using QuroErp.Application.Common.Codes;
using QuroErp.Application.Common.DocumentNumbers;
using QuroErp.Application.Common.I18n;
using QuroErp.Application.Mm.Inventory;
using QuroErp.Application.Mm.PurchaseOrders;
using QuroErp.Application.Mm.Warehouses;

namespace QuroErp.Application;

public static class DependencyInjection
{
    /// <summary>Application 서비스를 등록한다. TimeProvider와 Repository 구현은 호스트·Infrastructure가 등록한다.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IBusinessClock>(BusinessClock.Create);
        services.AddScoped<LoginService>();
        services.AddScoped<ApiKeyAuthenticator>();
        services.AddScoped<TranslationService>();
        services.AddScoped<ICodeValidator, CodeValidator>();
        services.AddScoped<CodeService>();
        services.AddScoped<CodeMajorService>();
        services.AddScoped<CodeMinorService>();
        services.AddScoped<PurchasePriceService>();
        services.AddScoped<CompanyLogoService>();
        services.AddScoped<CompanyService>();
        services.AddScoped<BusinessPlaceService>();
        services.AddScoped<FieldPolicyService>();
        services.AddScoped<AuthSettingService>();
        services.AddScoped<LocalizationService>();
        services.AddScoped<LocalizationSettingService>();
        services.AddScoped<CurrencyService>();
        services.AddScoped<ExchangeRateService>();
        services.AddScoped<TaxCodeService>();
        services.AddScoped<ITaxRateProvider, TaxAreaRateProvider>();
        services.AddScoped<InventorySettingService>();
        services.AddScoped<ItemService>();
        services.AddScoped<ItemImageService>();
        services.AddScoped<ItemBarcodeService>();
        services.AddScoped<WarehouseService>();
        services.AddScoped<PurchaseOrderService>();
        services.AddScoped<RoleService>();
        services.AddScoped<MenuService>();
        services.AddSingleton<MenuPermissionService>();
        services.AddScoped<UserProfileService>();
        services.AddScoped<UserService>();
        services.AddScoped<DocumentNumberService>();
        services.AddScoped<InventoryService>();
        return services;
    }
}
