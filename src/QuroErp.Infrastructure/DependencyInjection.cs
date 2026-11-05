using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuroErp.Application.Bc.BusinessPlaces;
using QuroErp.Application.Bc.Codes;
using QuroErp.Application.Bc.FieldPolicies;
using QuroErp.Application.Bc.Companies;
using QuroErp.Application.Bc.Items;
using QuroErp.Application.Bc.PurchasePrices;
using QuroErp.Application.Bc.SetItems;
using QuroErp.Application.Bc.SalesPrices;
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
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Common.Printing;
using QuroErp.Application.Mm.Inventory;
using QuroErp.Application.Mm.Lots;
using QuroErp.Application.Mm.GoodsReceipts;
using QuroErp.Application.Mm.PurchaseOrders;
using QuroErp.Application.Mm.Vendors;
using QuroErp.Application.Sd.Customers;
using QuroErp.Application.Mm.Warehouses;
using QuroErp.Infrastructure.Bc.BusinessPlaces;
using QuroErp.Infrastructure.Bc.Codes;
using QuroErp.Infrastructure.Bc.FieldPolicies;
using QuroErp.Infrastructure.Bc.Companies;
using QuroErp.Infrastructure.Bc.Items;
using QuroErp.Infrastructure.Bc.PurchasePrices;
using QuroErp.Infrastructure.Bc.SetItems;
using QuroErp.Infrastructure.Bc.SalesPrices;
using QuroErp.Infrastructure.Bc.Localization;
using QuroErp.Infrastructure.Bc.Tax;
using QuroErp.Infrastructure.Bc.Menus;
using QuroErp.Infrastructure.Bc.Roles;
using QuroErp.Infrastructure.Bc.UserProfiles;
using QuroErp.Infrastructure.Bc.Users;
using QuroErp.Infrastructure.Common.Auth;
using QuroErp.Infrastructure.Common.Codes;
using QuroErp.Infrastructure.Common.DocumentNumbers;
using QuroErp.Infrastructure.Common.I18n;
using QuroErp.Infrastructure.Common.Health;
using QuroErp.Infrastructure.Common.Persistence;
using QuroErp.Infrastructure.Reports;
using QuroErp.Infrastructure.Reports.Mm;
using QuroErp.Infrastructure.Mm.Inventory;
using QuroErp.Infrastructure.Mm.Lots;
using QuroErp.Infrastructure.Mm.GoodsReceipts;
using QuroErp.Infrastructure.Mm.PurchaseOrders;
using QuroErp.Infrastructure.Mm.Vendors;
using QuroErp.Infrastructure.Sd.Customers;
using QuroErp.Infrastructure.Mm.Warehouses;
using QuroErp.Application.Sd.SalesOrders;
using QuroErp.Infrastructure.Reports.Sd;
using QuroErp.Infrastructure.Sd.SalesOrders;
using QuroErp.Application.Sd.SalesInvoices;
using QuroErp.Application.Sd.Shipments;
using QuroErp.Infrastructure.Sd.SalesInvoices;
using QuroErp.Infrastructure.Sd.Shipments;

namespace QuroErp.Infrastructure;

public static class DependencyInjection
{
    /// <summary>연결 문자열 이름. 설정 키 ConnectionStrings:QuroErp (QuroERP.Database와 같다).</summary>
    public const string ConnectionStringName = "QuroErp";

    /// <summary>Infrastructure 서비스를 등록한다. 연결 문자열이 없으면 예외를 던진다.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"연결 문자열 ConnectionStrings:{ConnectionStringName}이 없다. user-secrets나 환경변수 ConnectionStrings__{ConnectionStringName}로 설정한다.");
        }

        // database.md "이름": snake_case 컬럼 → PascalCase 속성
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        services.AddNpgsqlDataSource(connectionString);
        services.AddSingleton<IDbConnectionFactory, NpgsqlConnectionFactory>();
        services.AddSingleton<IUnitOfWorkFactory, UnitOfWorkFactory>();
        services.AddSingleton<IAppUserRepository, AppUserRepository>();
        services.AddSingleton<IUserLoginLogRepository, UserLoginLogRepository>();
        services.AddSingleton<IAuthSettingRepository, AuthSettingRepository>();
        services.AddSingleton<IAutoLoginTokenRepository, AutoLoginTokenRepository>();
        services.AddSingleton<IPasswordHistoryRepository, PasswordHistoryRepository>();
        services.AddSingleton<IApiClientRepository, ApiClientRepository>();
        services.AddSingleton<IPasswordHashService, PasswordHashService>();
        services.AddSingleton<ITranslationRepository, TranslationRepository>();
        services.AddSingleton<ICodeRepository, CodeRepository>();
        services.AddSingleton<ICompanyRepository, CompanyRepository>();
        services.AddSingleton<IBusinessPlaceRepository, BusinessPlaceRepository>();
        services.AddSingleton<ILocalizationRepository, LocalizationRepository>();
        services.AddSingleton<ITaxRepository, TaxRepository>();
        services.AddSingleton<ITaxAreaRepository, TaxAreaRepository>();
        services.AddSingleton<ILocalizationSettingRepository, LocalizationSettingRepository>();
        services.AddSingleton<ICurrencyRepository, CurrencyRepository>();
        services.AddSingleton<IExchangeRateRepository, ExchangeRateRepository>();
        services.AddSingleton<ITaxCodeRepository, TaxCodeRepository>();
        services.AddSingleton<IInventorySettingRepository, InventorySettingRepository>();
        services.AddSingleton<IItemRepository, ItemRepository>();
        services.AddSingleton<IAlternateItemRepository, AlternateItemRepository>();
        services.AddSingleton<IAlternateItemGroupRepository, AlternateItemGroupRepository>();
        services.AddSingleton<IUserProfileRepository, UserProfileRepository>();
        services.AddSingleton<IUserRepository, UserRepository>();
        services.AddSingleton<IDocumentNumberRepository, DocumentNumberRepository>();
        services.AddSingleton<IInventoryRepository, InventoryRepository>();
        services.AddSingleton<ISetComponentRepository, SetComponentRepository>();
        services.AddSingleton<IPurchaseOrderRepository, PurchaseOrderRepository>();
        services.AddSingleton<IGoodsReceiptRepository, GoodsReceiptRepository>();
        services.AddSingleton<IWarehouseRepository, WarehouseRepository>();
        services.AddSingleton<IVendorRepository, VendorRepository>();
        services.AddSingleton<ICustomerRepository, CustomerRepository>();
        services.AddSingleton<ISalesOrderRepository, SalesOrderRepository>();
        services.AddSingleton<ISalesOrderDocument, SalesOrderDocument>();
        services.AddSingleton<IShipmentRepository, ShipmentRepository>();
        services.AddSingleton<IShipmentDocument, ShipmentDocument>();
        services.AddSingleton<ISalesInvoiceRepository, SalesInvoiceRepository>();
        services.AddSingleton<ISalesInvoiceDocument, SalesInvoiceDocument>();
        services.AddSingleton<ILotRepository, LotRepository>();
        services.AddSingleton<IRoleRepository, RoleRepository>();
        services.AddSingleton<IMenuRepository, MenuRepository>();
        services.AddSingleton<ICodeMajorRepository, CodeMajorRepository>();
        services.AddSingleton<ICodeMinorRepository, CodeMinorRepository>();
        services.AddSingleton<IPurchasePriceRepository, PurchasePriceRepository>();
        services.AddSingleton<ISetItemRepository, SetItemRepository>();
        services.AddSingleton<ISalesPriceRepository, SalesPriceRepository>();
        services.AddSingleton<IFieldPolicyRepository, FieldPolicyRepository>();
        services.AddSingleton<ICountryPackProvider, EmbeddedCountryPackProvider>();

        // Printing (reports.md): one Chromium per process, disposed with the container
        services.AddSingleton(PrintOptions.From(configuration));
        services.AddSingleton<PdfRenderer>();
        services.AddSingleton<IPdfRenderer>(sp => sp.GetRequiredService<PdfRenderer>());
        services.AddSingleton<IExcelWriter, ExcelWriter>();
        services.AddSingleton<IPurchaseOrderDocument, PurchaseOrderDocument>();
        services.AddSingleton<IPrintTemplateResolver>(_ => new PrintTemplateResolver());
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database")
            .AddCheck<ChromiumHealthCheck>("chromium");
        return services;
    }
}
