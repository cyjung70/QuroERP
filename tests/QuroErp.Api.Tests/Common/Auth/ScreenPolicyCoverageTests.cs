using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using QuroErp.Api.Common.Auth;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Api.Tests.Common.Auth;

/// <summary>
/// Every API change goes through a screen path policy (security.md "권한", TASK-CORE-059): no role checks are left, and every
/// policy path is one the seed gives the admin role Full Access to (ScreenPaths.All, checked against the seed in the database tests).
/// </summary>
public class ScreenPolicyCoverageTests
{
    // Changes that are not screen data: sign-in, the caller's own password and profile
    private static readonly string[] OwnChanges =
    [
        "AuthController.Login", "AuthController.Refresh", "AuthController.Logout", "AuthController.ChangePassword",
        "AuthController.AutoLogin", "UserProfileController.Update", "UserProfileController.RevokeTokens",
    ];

    private static IEnumerable<(Type Controller, MethodInfo Action)> Actions() =>
        typeof(ScreenPolicy).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
                .Select(m => (t, m)));

    private static IEnumerable<AuthorizeAttribute> AuthorizeOf(Type controller, MethodInfo action) =>
        controller.GetCustomAttributes<AuthorizeAttribute>().Concat(action.GetCustomAttributes<AuthorizeAttribute>());

    private static string? ScreenPathOf(Type controller, MethodInfo action) =>
        AuthorizeOf(controller, action).Select(a => a.Policy).FirstOrDefault(p => p?.StartsWith(ScreenPolicy.Prefix, StringComparison.Ordinal) == true)
            ?[ScreenPolicy.Prefix.Length..];

    [Fact, Trait("Id", "BC-UT-214")]
    public void NoActionChecksARole()
    {
        Actions().Where(a => AuthorizeOf(a.Controller, a.Action).Any(x => !string.IsNullOrEmpty(x.Roles)))
            .Select(a => $"{a.Controller.Name}.{a.Action.Name}").Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UT-214")]
    public void EveryChange_HasAScreenPolicy_OnASeededPath()
    {
        var changes = Actions()
            .Where(a => a.Action.GetCustomAttributes<HttpMethodAttribute>().SelectMany(h => h.HttpMethods).Any(m => m is not ("GET" or "HEAD")))
            .Where(a => !OwnChanges.Contains($"{a.Controller.Name}.{a.Action.Name}"))
            .ToList();

        changes.Where(a => ScreenPathOf(a.Controller, a.Action) is null)
            .Select(a => $"{a.Controller.Name}.{a.Action.Name}").Should().BeEmpty();
        changes.Select(a => ScreenPathOf(a.Controller, a.Action)).Distinct().Should().BeSubsetOf(ScreenPaths.All);
    }

    [Fact, Trait("Id", "BC-UT-214")]
    public void ScreenOnlyReads_HaveTheirScreenPolicy()
    {
        var reads = Actions().Where(a => ScreenPathOf(a.Controller, a.Action) is not null
                && a.Action.GetCustomAttributes<HttpMethodAttribute>().SelectMany(h => h.HttpMethods).Contains("GET"))
            .Select(a => $"{a.Controller.Name}.{a.Action.Name}");

        reads.Should().BeEquivalentTo(
            "UserController.Search", "UserController.Get", "UserController.Previous", "UserController.Next",
            "RoleController.GetMenus", "RoleController.GetUnassignedMenus",
            "PurchaseOrderController.Search", "PurchaseOrderController.Get",
            "GoodsReceiptController.Search", "GoodsReceiptController.Get", "GoodsReceiptController.OpenPoLines",
            "SupplierInvoiceController.Search", "SupplierInvoiceController.Get", "SupplierInvoiceController.OpenReceiptLines",
            "PurchaseOrderController.PrintPdf", "PurchaseOrderController.PrintXlsx");
    }
}
