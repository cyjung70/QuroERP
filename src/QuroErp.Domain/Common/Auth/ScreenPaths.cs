namespace QuroErp.Domain.Common.Auth;

/// <summary>
/// Screen paths that guard APIs (security.md "권한", app-shell.md 3.2): an API answers a caller whose role has a menu in use
/// with this <c>program_id</c>. Each path here has a screen menu and an admin Full Access row in the seed.
/// </summary>
public static class ScreenPaths
{
    public const string Item = "/bc/item";
    public const string Code = "/bc/code";
    public const string CodeMajor = "/bc/code-major";
    public const string ItemBarcode = "/bc/item-barcode";
    public const string PurchasePrice = "/bc/purchase-price";
    public const string PurchaseOrder = "/mm/purchase-order";
    public const string Company = "/bc/company";
    public const string Currency = "/bc/currency";
    public const string User = "/bc/user";
    public const string CompanyMenu = "/bc/company-menu";
    public const string RoleMenu = "/bc/role-menu";
    public const string ExchangeRate = "/bc/exchange-rate";
    public const string TaxCode = "/bc/tax-code";
    public const string TaxArea = "/bc/tax-area";
    public const string LocalizationSetting = "/bc/localization-setting";
    public const string SetItem = "/bc/set-item";
    public const string SalesPrice = "/bc/sales-price";

    /// <summary>Every path above. A test checks the seed gives the admin role Full Access to each.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Item, Code, CodeMajor, ItemBarcode, PurchasePrice, PurchaseOrder, Company, Currency, User, CompanyMenu, RoleMenu,
        ExchangeRate, TaxCode, TaxArea, LocalizationSetting, SetItem, SalesPrice,
    ];
}
