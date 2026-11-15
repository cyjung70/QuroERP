using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuroErp.Contracts.Bc.FieldPolicies;
using QuroErp.Contracts.Bc.Items;
using QuroErp.Contracts.Bc.Localization;
using QuroErp.Contracts.Bc.UserProfiles;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Contracts.Common.Codes;
using QuroErp.Contracts.Mm.Locations;
using QuroErp.Contracts.Mm.Warehouses;
using QuroErp.Contracts.Sd.Assemblies;
using QuroErp.Web.Auth;
using QuroErp.Web.Bc.Codes;
using QuroErp.Web.Bc.Items;
using QuroErp.Web.Bc.Localization;
using QuroErp.Web.Bc.UserProfiles;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Pages.Sd;
using QuroErp.Web.Components.Pages.Sd.Models;
using QuroErp.Web.Components.Screens;
using QuroErp.Web.Mm.Locations;
using QuroErp.Web.Mm.Warehouses;
using QuroErp.Web.Sd.Assemblies;
using QuroErp.Web.Tests.Screens;

namespace QuroErp.Web.Tests.Sd;

/// <summary>Set assembly screen /sd/assembly (SD-UC-055, TASK-SD-073) against a fake API. The company is US (base USD).</summary>
public class AssemblyScreenTests : BunitContext
{
    private const string Screen = "assembly";
    private readonly FakeAssemblyApi _api = new();
    private readonly FakeConfirm _confirm = new();
    private readonly BunitAuthorizationContext _auth;

    public AssemblyScreenTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("ko");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddWebLocalization();
        Services.AddCompanyTime();
        Services.AddSingleton<IQuroConfirm>(_confirm);
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton<AccessTokenStore>();
        Services.AddScoped<AccessTokenProvider>();
        Services.AddHttpClient<AuthApiClient>(http => http.BaseAddress = new Uri("http://api.test"));
        Services.AddHttpClient<AssemblyApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<ItemApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<LocalizationApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<WarehouseApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<UserProfileApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<LocationApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<CodeApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<FieldPolicyApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddScoped<FieldPolicyCache>();
        _auth = AddAuthorization();
        Services.AddScoped<IScreenAccess, ScreenAccess>();
    }

    [Fact, Trait("Id", "SD-UC-055-T01"), Trait("Rule", "MM-BR-703")]
    public void New_StartsTodayInTheUsersWarehouse_AtLocationStar_WithVoidAndDisassembleDisabled()
    {
        var page = RenderAs("admin");

        page.Find("[data-testid=assembly-doc-no]").TextContent.Should().Be("새 문서");
        page.Field("assembly-header-warehouse-code").Value.Should().Be("MAIN");
        Frame(page).Header.LocationCode.Should().Be("*");
        Frame(page).Header.AssemblyDt.Should().Be(DateOnly.FromDateTime(DateTime.Today));
        Enabled(page).Should().BeEquivalentTo("search", "new", "save");
        Button(page, "void").HasAttribute("disabled").Should().BeTrue("a blocked command is disabled, not hidden");
        Button(page, "disassemble").HasAttribute("disabled").Should().BeTrue();
        Button(page, "add-row").HasAttribute("disabled").Should().BeTrue("the system picks the component rows");
    }

    [Fact, Trait("Id", "SD-UC-055-T01"), Trait("Id", "SD-UT-518"), Trait("Rule", "SD-BR-518")]
    public void SetAndQty_ShowTheComponentsFifoParts_ReadOnly_WithTheirAmount()
    {
        var page = RenderAs("admin");

        EnterSet(page, "set01", 2m);

        page.WaitForAssertion(() => Lines(page).Rows.Should().HaveCount(3));
        Rows(page).Select(l => (l.ItemCd, l.ComponentQty, l.Qty, l.LocationCode, l.LotNo, l.Amt)).Should().Equal(
            ("CABLE", 1m, 1m, "A1", "L1", 12m), ("CABLE", 1m, 1m, "*", "L2", 12m), ("BOLT", 4m, 8m, "*", "*", 8m));
        Frame(page).Header.SetItemName.Should().Be("Cable Set");
        page.Find("[data-testid=assembly-totals-amt]").TextContent.Should().Contain("32.00").And.Contain("USD");
        _api.Requests.Should().Contain(r => r.RequestUri!.PathAndQuery.StartsWith("/api/sd/assemblies/preview?setItemId=30&qty=2&warehouseId=1", StringComparison.Ordinal));
        Frame(page).IsLineLocked!(Rows(page)[0]).Should().BeTrue();
    }

    [Fact, Trait("Id", "SD-UC-055-T03"), Trait("Id", "SD-UT-514"), Trait("Rule", "SD-BR-514")]
    public void ShortComponent_GetsARowWithTheShortQty_TheCellMark_AndAMessage()
    {
        var page = RenderAs("admin");

        EnterSet(page, "SET01", 5m);

        page.WaitForAssertion(() => Rows(page).Should().Contain(l => l.ShortQty == 3m));
        var shortRow = Rows(page).Single(l => l.ShortQty > 0);
        (shortRow.ItemCd, shortRow.Qty, shortRow.LocationCode, shortRow.LotNo).Should().Be(("CABLE", 3m, null, null));
        page.Find("[data-testid=assembly-message]").GetAttribute("data-level").Should().Be("error");
        page.Find("[data-testid=assembly-totals-short-count]").TextContent.Should().Contain("1");
    }

    [Fact, Trait("Rule", "SD-BR-514")]
    public void ItemThatIsNotASet_IsRefusedWithAMessage_AndNoPreview()
    {
        var page = RenderAs("admin");

        EnterSet(page, "BOLT", 1m);

        page.WaitForAssertion(() => page.Find("[data-testid=assembly-message]").TextContent.Should().Contain("Set 품목이 아닙니다"));
        Frame(page).Header.SetItemId.Should().BeNull();
        _api.Requests.Should().NotContain(r => r.RequestUri!.AbsolutePath == "/api/sd/assemblies/preview");
    }

    [Fact]
    public void SetLookup_ShowsOnlySetItems()
    {
        var page = RenderAs("admin");

        page.Find("[data-testid=assembly-header-set-item-lookup]").Click();

        page.WaitForAssertion(() => page.LookupRows("assembly-set-lookup").Should().ContainSingle().Which.TextContent.Should().Contain("SET01"));
        page.PickLookupRow<ItemDto, Assembly>("assembly-set-lookup", 0);
        page.WaitForAssertion(() => Frame(page).Header.SetItemId.Should().Be(30));
    }

    [Fact, Trait("Id", "SD-UC-055-T01"), Trait("Rule", "SD-BR-514")]
    public void Save_PostsTheHeader_AndOpensTheSavedAssembly_ReadOnly()
    {
        var page = RenderAs("admin");
        EnterSet(page, "SET01", 2m);
        page.WaitForAssertion(() => Lines(page).Rows.Should().HaveCount(3));
        page.Field("assembly-header-location-code").Change("A1");

        Button(page, "save").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=assembly-doc-no]").TextContent.Should().Be("AS2611000001"));
        _api.Saved.Should().ContainSingle().Which.Should().Be(
            new SaveAssemblyRequest(30, 2m, DateOnly.FromDateTime(DateTime.Today), 1, 21, null));
        Frame(page).Header.SetLotNo.Should().Be("261116-001");
        Enabled(page).Should().BeEquivalentTo("search", "new", "disassemble", "void");
        page.Field("assembly-header-qty").IsReadOnly.Should().BeTrue("a saved assembly does not change");
    }

    [Fact, Trait("Id", "SD-UC-055-T04"), Trait("Id", "SD-UT-516"), Trait("Rule", "SD-BR-516")]
    public void Disassemble_OffersTheQtyLeft_AndOpensTheNewDisassembly()
    {
        var page = Open();

        Button(page, "disassemble").Click();
        page.WaitForElement("[data-testid=assembly-disassemble]");
        page.Find("[data-testid=assembly-disassemble]").TextContent.Should().Contain("2.00");
        page.Find("[data-testid=assembly-disassemble-qty]").Change("1");
        page.Find("[data-testid=assembly-disassemble-ok]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=assembly-doc-no]").TextContent.Should().Be("AS2611000002"));
        _api.Disassembled.Should().ContainSingle().Which.Should().Be(new DisassembleRequest(1m, DateOnly.FromDateTime(DateTime.Today), null));
        Frame(page).Header.SourceAssemblyNo.Should().Be("AS2611000001");
        Button(page, "disassemble").HasAttribute("disabled").Should().BeTrue("a disassembly is not taken apart");
    }

    [Fact, Trait("Id", "SD-UC-055-T05"), Trait("Rule", "SD-BR-516"), Trait("Rule", "MM-BR-601")]
    public void Void_AsksFirst_AndAnAssemblyWithADisassembly_ShowsSdE516()
    {
        _api.VoidProblem = """{"status":409,"code":"SD-E-516","args":["AS2611000001",1]}""";
        var page = Open();
        _confirm.Answer = true;

        Button(page, "void").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=assembly-message]").TextContent.Should().Contain("AS2611000001"));
        _confirm.Questions.Should().ContainSingle().Which.Should().Contain("취소할까요");
        Frame(page).Header.Status.Should().Be("OP");
    }

    [Fact, Trait("Rule", "MM-BR-601")]
    public void Void_ShowsTheVoidRow()
    {
        var page = Open();
        _confirm.Answer = true;

        Button(page, "void").Click();

        page.WaitForAssertion(() => Frame(page).Header.Status.Should().Be("VO"));
        _api.Voided.Should().ContainSingle();
        Button(page, "disassemble").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact, Trait("Rule", "BC-BR-214")]
    public void WithoutWriteAccess_OnlySearchAndNewAreEnabled()
    {
        var page = RenderAs("user");

        Enabled(page).Should().BeEquivalentTo("search", "new");
        page.Find("[data-testid=assembly-header-set-item-lookup]").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void Labels_FollowTheLanguage()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("en");

        var page = RenderAs("admin");

        page.Find("[data-testid=assembly-title] h1").TextContent.Should().Be("Set Assembly");
        Lines(page).Headers.Select(h => h.Title).Should().Contain(["Component code", "Qty per Set", "Location", "Lot"]);
        page.Find("[data-testid=assembly-header]").TextContent.Should().Contain("Receiving Location").And.Contain("Set Lot");
    }

    private IRenderedComponent<Assembly> RenderAs(string role)
    {
        var store = Services.GetRequiredService<AccessTokenStore>();
        var sessionId = store.Add(new LoginResponse($"token-{role}", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", role, false, "ko"));
        _auth.SetAuthorized("kim");
        _auth.SetRoles(role);
        _auth.SetClaims(new Claim(WebAuthClaims.SessionId, sessionId));
        var page = Render<Assembly>();
        page.WaitForElement("[data-testid=assembly-header-set-item-cd]");
        return page;
    }

    // The saved assembly AS2611000001 of 2 Sets, opened from the find dialog
    private IRenderedComponent<Assembly> Open()
    {
        var page = RenderAs("admin");
        Button(page, "search").Click();
        page.WaitForAssertion(() => page.LookupRows("assembly-find").Should().NotBeEmpty());
        page.PickLookupRow<AssemblyHeader, Assembly>("assembly-find", 0);
        page.WaitForAssertion(() => page.Find("[data-testid=assembly-doc-no]").TextContent.Should().Be("AS2611000001"));
        return page;
    }

    private static void EnterSet(IRenderedComponent<Assembly> page, string code, decimal qty)
    {
        page.Field("assembly-header-qty").Change(qty.ToString(CultureInfo.InvariantCulture));
        page.Field("assembly-header-set-item-cd").Change(code);
    }

    private static List<AssemblyLine> Rows(IRenderedComponent<Assembly> page)
    {
        var lines = Frame(page).Lines;
        return [.. lines.RowKeys.Select(lines.GetItem)];
    }

    private static DataGridPage<AssemblyLine> Lines(IRenderedComponent<Assembly> page) => page.DataGrid<AssemblyLine, Assembly>("assembly-line");

    private static QuroHeaderDetail<AssemblyHeader, AssemblyLine> Frame(IRenderedComponent<Assembly> page) =>
        page.FindComponent<QuroHeaderDetail<AssemblyHeader, AssemblyLine>>().Instance;

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<Assembly> page, string action) =>
        page.Find($"[data-testid={Screen}-toolbar-{action}], [data-testid={Screen}-line-{action}]");

    private static IReadOnlyList<string> Enabled(IRenderedComponent<Assembly> page) =>
        [.. page.FindAll("[role=toolbar] button:not([disabled])").Select(b => b.GetAttribute("data-testid")!.Replace($"{Screen}-toolbar-", "").Replace($"{Screen}-line-", ""))
            .Where(b => b != "show-all-fields")];

    private static void Configure(HttpClient http) => http.BaseAddress = new Uri("http://api.test");

    /// <summary>Assembly, item, localization, warehouse, profile, Location, code and field policy API.</summary>
    private sealed class FakeAssemblyApi : HttpMessageHandler
    {
        private static readonly DateTimeOffset Updated = new(2026, 11, 16, 1, 0, 0, TimeSpan.Zero);

        private static readonly ItemDto[] Items =
        [
            Item(11, "BOLT", "Hex Bolt", "INVENTORY"),
            Item(12, "CABLE", "Cable Reel", "INVENTORY"),
            Item(30, "SET01", "Cable Set", "SET"),
        ];

        public List<HttpRequestMessage> Requests { get; } = [];

        public List<SaveAssemblyRequest> Saved { get; } = [];

        public List<DisassembleRequest> Disassembled { get; } = [];

        public List<DateTimeOffset> Voided { get; } = [];

        public string? VoidProblem { get; set; }

        private string _status = "OP";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var path = request.RequestUri!.AbsolutePath;
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
            if (path.StartsWith("/api/codes/", StringComparison.Ordinal))
            {
                return Json(new List<CodeItemDto>());
            }
            if (path.StartsWith("/" + FieldPolicyRoutes.Base, StringComparison.Ordinal))
            {
                return Json(new List<FieldPolicyDto>());
            }
            switch (path)
            {
                case "/" + LocalizationRoutes.Base:
                    return Json(new LocalizationDto("US", "USD", "en", "America/New_York", "SALES_TAX", "MM/dd/yyyy", "LINE", [],
                        [new CurrencyDto("USD", "US Dollar", 2, "US")], [new CountryDecimalDto("US", "MM", 2)], [new DecimalSettingDto("MM", 2, "HALF_UP")]));
                case "/" + WarehouseRoutes.Options:
                    return Json(new List<WarehouseOptionDto> { new(1, "MAIN", "Main Store", "NORMAL", true) });
                case "/" + WarehouseRoutes.Base:
                    return Json(new List<WarehouseDto> { new(1, "MAIN", "Main Store", true, true, "NORMAL", null, Updated) });
                case "/" + UserProfileRoutes.Base:
                    return Json(new UserProfileDto("kim", "Kim", null, null, "ko", 1, "MAIN", "Main Store", Updated));
                case "/" + LocationRoutes.Options:
                    return Json(new List<LocationOptionDto> { new(20, "*", null), new(21, "A1", "Aisle 1") });
                case "/" + ItemRoutes.Base:
                    var code = query["itemCd"];
                    var name = query["itemName"];
                    return Json(new ItemListDto([.. Items.Where(i =>
                        (string.IsNullOrEmpty(code) || i.ItemCd.StartsWith(code, StringComparison.OrdinalIgnoreCase))
                        && (string.IsNullOrEmpty(name) || (i.ItemName ?? "").Contains(name, StringComparison.OrdinalIgnoreCase)))], Items.Length));
                case "/api/sd/assemblies/preview":
                    return Json(Preview(decimal.Parse(query["qty"]!, CultureInfo.InvariantCulture)));
                case "/api/sd/assemblies" when request.Method == HttpMethod.Get:
                    return Json(new List<AssemblySummaryDto> { Summary(1001, "AS2611000001", "ASSEMBLE", null) });
                case "/api/sd/assemblies" when request.Method == HttpMethod.Post:
                    Saved.Add((await request.Content!.ReadFromJsonAsync<SaveAssemblyRequest>(cancellationToken))!);
                    return Json(new AssemblyCreatedDto(1001, "AS2611000001", "261116-001"), HttpStatusCode.Created);
                case "/api/sd/assemblies/1001/disassemble":
                    Disassembled.Add((await request.Content!.ReadFromJsonAsync<DisassembleRequest>(cancellationToken))!);
                    return Json(new AssemblyCreatedDto(1002, "AS2611000002", "261116-001"), HttpStatusCode.Created);
                case "/api/sd/assemblies/1001/void":
                    if (VoidProblem is not null)
                    {
                        return new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent(VoidProblem, null, "application/problem+json") };
                    }
                    Voided.Add((await request.Content!.ReadFromJsonAsync<VoidAssemblyRequest>(cancellationToken))!.UpdatedAt);
                    _status = "VO";
                    return Json(Summary(1001, "AS2611000001", "ASSEMBLE", null));
                case "/api/sd/assemblies/1001":
                    return Json(new AssemblyDto(Summary(1001, "AS2611000001", "ASSEMBLE", null), null, Lines()));
                case "/api/sd/assemblies/1002":
                    return Json(new AssemblyDto(Summary(1002, "AS2611000002", "DISASSEMBLE", "AS2611000001") with { Qty = 1m, QtyLeft = 0m }, null, Lines()));
                default:
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        }

        private AssemblySummaryDto Summary(long id, string no, string kind, string? source) => new(
            id, no, kind, new DateOnly(2026, 11, 16), 30, "SET01", "Cable Set", 2m, 1, "MAIN", 20, "*", "261116-001",
            source is null ? null : 1001, source, _status, 32m, kind == "ASSEMBLE" && _status == "OP" ? 2m : 0m, Updated);

        private static List<AssemblyLineDto> Lines() =>
        [
            new(1, 12, "CABLE", "Cable Reel", 2m, 21, "A1", 1, "L1", 12m, 24m),
            new(2, 11, "BOLT", "Hex Bolt", 8m, 20, "*", 2, "*", 1m, 8m),
        ];

        // CABLE (1 per Set): 1 in A1 lot L1 and 1 in '*' lot L2, the rest short; BOLT (4 per Set) on ('*', '*') at 1
        private static AssemblyPreviewDto Preview(decimal qty)
        {
            var first = Math.Min(qty, 1m);
            var second = Math.Min(qty - first, 1m);
            var cable = new List<AssemblyPreviewPickDto> { new(21, "A1", 1, "L1", first, 12m, first * 12m) };
            if (second > 0)
            {
                cable.Add(new AssemblyPreviewPickDto(20, "*", 2, "L2", second, 12m, second * 12m));
            }
            var components = new List<AssemblyPreviewComponentDto>
            {
                new(12, "CABLE", "Cable Reel", 1m, qty, qty - first - second, cable),
                new(11, "BOLT", "Hex Bolt", 4m, qty * 4, 0m, [new(20, "*", 3, "*", qty * 4, 1m, qty * 4)]),
            };
            return new AssemblyPreviewDto(30, qty, 1, components, components.Sum(c => c.Picks.Sum(p => p.Amt)));
        }

        private static ItemDto Item(long id, string code, string name, string type) => new(
            id, code, name, null, null, type, "EA", null, "Y", 0m, 10m, null, null, 0m, null, null, null, null, null, null, true,
            "TAXABLE", "", 0m, 0m, 0m, Updated, "setup", Updated, "kim");

        private static HttpResponseMessage Json<T>(T value, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = JsonContent.Create(value) };
    }
}
