using QuroErp.Contracts.Common.Auth;

namespace QuroErp.Web.Components.Pages;

/// <summary>State of a home step or shortcut (app-shell.md 6); the value is the element's data-state.</summary>
public enum HomeStepState
{
    /// <summary>The screen exists and the user's role has its menu: a link.</summary>
    Ready,

    /// <summary>The screen exists but the user's role has no menu for it.</summary>
    NoPermission,

    /// <summary>The screen is not built yet (phase 2).</summary>
    NotReady,
}

/// <summary>A process step or shortcut on the home page.</summary>
/// <param name="Id">Test ID suffix and resource key part, e.g. purchase-order.</param>
/// <param name="ScreenReady">True once the screen task is done; that task sets it.</param>
public sealed record HomeStep(string Id, string Path, string ResourceKey, bool ScreenReady);

/// <summary>The home page steps and shortcuts (app-shell.md 6), and the rule for their state.</summary>
public static class HomeSteps
{
    private const string ProgramType = "P";

    public static readonly IReadOnlyList<HomeStep> PurchaseFlow =
    [
        new("purchase-order", "/mm/purchase-order", "Home.Step.PurchaseOrder", false),
        new("goods-receipt", "/mm/goods-receipt", "Home.Step.GoodsReceipt", false),
        new("supplier-invoice", "/mm/supplier-invoice", "Home.Step.SupplierInvoice", false),
        new("ap-payment", "/mm/ap-payment", "Home.Step.ApPayment", false),
    ];

    public static readonly IReadOnlyList<HomeStep> SalesFlow =
    [
        new("sales-order", "/sd/sales-order", "Home.Step.SalesOrder", false),
        new("shipment", "/sd/shipment", "Home.Step.Shipment", false),
        new("sales-invoice", "/sd/sales-invoice", "Home.Step.SalesInvoice", false),
        new("ar-receipt", "/sd/ar-receipt", "Home.Step.ArReceipt", false),
    ];

    public static readonly IReadOnlyList<HomeStep> MasterLinks =
    [
        new("item", "/bc/item", "Home.Link.Item", true),
        new("customer", "/sd/customer", "Home.Link.Customer", false),
        new("vendor", "/mm/vendor", "Home.Link.Vendor", false),
        new("stock", "/mm/report-item-list", "Home.Link.Stock", false),
    ];

    public static readonly IReadOnlyList<HomeStep> SettingsLinks =
    [
        new("company", "/bc/company", "Home.Link.Company", true),
    ];

    /// <summary>Not ready until the screen exists; then ready when a P menu of the user opens its path.</summary>
    public static HomeStepState StateOf(HomeStep step, IEnumerable<AuthMenu> menus) =>
        !step.ScreenReady ? HomeStepState.NotReady
        : menus.Any(m => m.Type == ProgramType && SamePath(m.ProgramId, step.Path)) ? HomeStepState.Ready
        : HomeStepState.NoPermission;

    /// <summary>The data-state attribute value.</summary>
    public static string DataState(HomeStepState state) => state switch
    {
        HomeStepState.Ready => "ready",
        HomeStepState.NoPermission => "no-permission",
        _ => "not-ready",
    };

    private static bool SamePath(string? programId, string path) =>
        programId is not null && string.Equals(programId.Trim().TrimEnd('/'), path, StringComparison.OrdinalIgnoreCase);
}
