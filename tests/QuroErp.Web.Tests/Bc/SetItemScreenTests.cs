using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuroErp.Contracts.Bc.Items;
using QuroErp.Contracts.Bc.Localization;
using QuroErp.Contracts.Bc.SetItems;
using QuroErp.Contracts.Common;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Web.Auth;
using QuroErp.Web.Bc.Items;
using QuroErp.Web.Bc.Localization;
using QuroErp.Web.Bc.SetItems;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Pages.Bc;
using QuroErp.Web.Components.Pages.Bc.Models;
using QuroErp.Web.Components.Screens;
using QuroErp.Web.Tests.Screens;

namespace QuroErp.Web.Tests.Bc;

/// <summary>Set item composition screen /bc/set-item (BC-UC-013, TASK-BC-026) against a fake item and set item API.</summary>
public class SetItemScreenTests : BunitContext
{
    private static readonly DateTimeOffset Read = new(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);

    private readonly FakeApi _api = new();
    private readonly BunitAuthorizationContext _auth;

    public SetItemScreenTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("ko");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddWebLocalization();
        Services.AddCompanyTime();
        Services.AddSingleton<IQuroConfirm>(new FakeConfirm());
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton<AccessTokenStore>();
        Services.AddScoped<AccessTokenProvider>();
        Services.AddHttpClient<AuthApiClient>(Configure);
        Services.AddHttpClient<SetItemApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<ItemApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<LocalizationApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        _auth = AddAuthorization();
        // Screens ask IScreenAccess, which reads the role from this test authorization (security.md "화면 쓰기 권한")
        Services.AddScoped<IScreenAccess, ScreenAccess>();
    }

    [Fact]
    public void Open_ShowsTheSpecColumns_AndTheRequiredSetCode_AndSearchesNothingYet()
    {
        var page = RenderAs("admin");

        Grid(page).Headers.Select(h => h.Title).Should().ContainInOrder("구성품", "구성품명", "수량");
        page.Find("[data-testid=set-item-search-item-cd]").GetAttribute("aria-required").Should().Be("true");
        _api.SetReads.Should().BeEmpty();
    }

    [Fact, Trait("Id", "SD-UT-501")]
    public void Search_WithoutASetCode_AsksForIt()
    {
        var page = RenderAs("admin");

        page.Find("[data-testid=set-item-toolbar-search]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=set-item-message]").TextContent.Should().Be("세트 품목을(를) 입력하세요."));
        _api.SetReads.Should().BeEmpty();
    }

    [Theory, Trait("Id", "CORE-060-WT-03")]
    [InlineData("admin", true)]
    [InlineData("user", false)]
    public void Grid_ColorsItsCells_AndNamesItsRequiredColumns_OnlyWhenTheUserCanEdit(string role, bool canEdit)
    {
        var page = SearchKit(RenderAs(role));

        var host = page.Find("[data-testid=set-item-grid]");
        host.ClassList.Contains("quro-grid-entry").Should().Be(canEdit);
        var style = host.QuerySelector("style")?.TextContent;
        if (canEdit)
        {
            // Component and quantity are required; the name is a read-only lookup column (gray from aria-readonly)
            style.Should().Contain($"#{host.Id} .quro-body-cell[data-field=\"itemCd\"]").And.Contain("[data-field=\"qty\"]")
                .And.NotContain("itemName");
            Grid(page).IsReadOnly(0, "itemName").Should().BeTrue();
        }
        else
        {
            style.Should().BeNull();
        }
    }

    [Fact, Trait("Id", "BC-UC-013-T03")]
    public void Search_InLowerCase_ShowsTheComponentsInSequenceOrder_TheSetName_AndTheSdQtyDecimals()
    {
        var page = SearchKit(RenderAs("admin"));

        Grid(page).Column("itemCd").Should().Equal("BOLT", "NUT");
        Grid(page).Column("itemName").Should().Equal("Bolt", "Nut");
        Grid(page).Column("qty").Should().Equal("2.00", "1.50");
        page.Find("[data-testid=set-item-search-item-nm]").GetAttribute("value").Should().Be("Gift kit");
        _api.SetReads.Should().Equal("KIT01");
    }

    [Fact, Trait("Id", "SD-UT-501")]
    public void Search_OfAnItemThatIsNotASet_ShowsSdE501()
    {
        var page = RenderAs("admin");
        page.Find("[data-testid=set-item-search-item-cd]").Change("BOLT");

        page.Find("[data-testid=set-item-toolbar-search]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=set-item-message]").TextContent.Should().Contain("BOLT"));
        Grid(page).Rows.Should().BeEmpty();
    }

    [Fact, Trait("Id", "SD-UT-501")]
    public void SetLookup_ListsOnlySetItemsInUse_AndPickingOneSearches()
    {
        var page = RenderAs("admin");

        page.Find("[data-testid=set-item-search-find-set]").Click();
        page.WaitForAssertion(() => page.LookupRows("set-item-set-lookup").Should().HaveCount(2));
        LookupTexts(page, "set-item-set-lookup").Should().SatisfyRespectively(r => r.Should().Contain("KIT01"), r => r.Should().Contain("KIT02"));
        page.PickLookupRow<ItemDto, SetItem>("set-item-set-lookup", 0);

        page.WaitForAssertion(() => Grid(page).Column("itemCd").Should().Equal("BOLT", "NUT"));
        page.Find("[data-testid=set-item-search-item-cd]").GetAttribute("value").Should().Be("KIT01");
        _api.SetReads.Should().Equal("KIT01");
    }

    [Fact, Trait("Id", "SD-UT-502"), Trait("Id", "MM-UT-361")]
    public void TypedComponentCode_FillsTheName_ACodeThatCannotBeAComponentIsNotTaken_AndAPastedOneIsMarkedAndBlocksTheSave()
    {
        var page = SearchKit(RenderAs("admin"));
        page.Find("[data-testid=set-item-toolbar-add-row]").Click();

        Grid(page).Edit(2, "itemCd", "washer").Should().BeTrue();
        Grid(page).Edit(2, "qty", "3");

        Grid(page).Text(2, "itemCd").Should().Be("WASHER");
        Grid(page).Text(2, "itemName").Should().Be("Washer");
        // A typed code without a match keeps the cell in edit (A-05)
        Grid(page).Edit(2, "itemCd", "FREIGHT").Should().BeFalse();
        Grid(page).Text(2, "itemCd").Should().Be("WASHER");

        string[] codes = ["FREIGHT", "KIT02", "OLDB", "NOPE"];
        foreach (var _ in codes)
        {
            page.Find("[data-testid=set-item-toolbar-add-row]").Click();
        }
        Grid(page).Paste(3, "itemCd", [.. codes.Select(c => new[] { c })]).Should().BeTrue();
        Enumerable.Range(3, codes.Length).Should().OnlyContain(i =>
            Grid(page).Rows[i].QuerySelector("[data-field=itemCd]")!.GetAttribute("aria-invalid") == "true");
        Grid(page).Rows[2].QuerySelector("[data-field=itemCd]")!.GetAttribute("aria-invalid").Should().BeNull();
        page.Find("[data-testid=set-item-toolbar-save]").Click();
        page.WaitForAssertion(() => page.Find("[data-testid=set-item-message]").TextContent.Should().Be("없는 코드입니다."));
        _api.Saved.Should().BeEmpty();
    }

    [Fact, Trait("Id", "SD-UT-502"), Trait("Id", "MM-UT-361")]
    public async Task F4_OpensTheComponentLookup_WithInventoryItemsInUse_LotManagedToo_AndThePickFillsTheRow()
    {
        var page = SearchKit(RenderAs("admin"));
        page.Find("[data-testid=set-item-toolbar-add-row]").Click();

        var popup = Grid(page).F4Async(2, "itemCd", "");
        page.WaitForAssertion(() => page.LookupRows("set-item-lookup").Should().HaveCount(4));
        LookupTexts(page, "set-item-lookup").Should().SatisfyRespectively(
            r => r.Should().Contain("BOLT"), r => r.Should().Contain("LOTB"), r => r.Should().Contain("NUT"), r => r.Should().Contain("WASHER"));
        page.PickLookupRow<LookupRow, SetItem>("set-item-lookup", 3);

        (await popup).Should().BeTrue();
        Grid(page).Text(2, "itemCd").Should().Be("WASHER");
        Grid(page).Text(2, "itemName").Should().Be("Washer");
    }

    [Fact, Trait("Id", "BC-UC-013-T01"), Trait("Id", "SD-UT-505")]
    public void AddChangeAndDelete_AreSavedTogether_ForTheSetThatWasRead()
    {
        var page = SearchKit(RenderAs("admin"));

        Grid(page).Edit(1, "qty", "4");
        Grid(page).Select(0);
        page.Find("[data-testid=set-item-toolbar-mark-delete]").Click();
        Grid(page).Select(1);
        page.Find("[data-testid=set-item-toolbar-add-row]").Click();
        Grid(page).Edit(2, "itemCd", "WASHER");
        Grid(page).Edit(2, "qty", "6");
        page.Find("[data-testid=set-item-search-item-cd]").Change("KIT02");

        page.Find("[data-testid=set-item-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=set-item-message]").TextContent.Should().Be("저장했습니다."));
        var (setCd, request) = _api.Saved.Should().ContainSingle().Subject;
        setCd.Should().Be("KIT01");
        request.Added.Should().Equal(new SetComponentRowRequest(null, "WASHER", 6, null));
        request.Updated.Should().Equal(new SetComponentRowRequest(12, "NUT", 4, Read));
        request.Deleted.Should().Equal(new SetComponentDeleteRequest(11, Read));
    }

    [Theory, Trait("Id", "BC-UC-013-T02"), Trait("Id", "SD-UT-506")]
    [InlineData("SD-E-504", "구성품 NUT이(가) 세트에 두 번 들어 있습니다. 한 세트에 같은 구성품은 한 번만 넣을 수 있습니다.")]
    [InlineData("SD-E-502", "품목 NUT은(는) 구성품이 될 수 없습니다. 구성품은 사용 중인 재고(Inventory) 품목이어야 합니다.")]
    public void RejectedRow_IsNamedInTheMessage_AndSelected_AndTheGridKeepsTheChanges(string code, string message)
    {
        _api.SaveProblem = (HttpStatusCode.UnprocessableEntity, $$"""{"status":422,"code":"{{code}}","args":["NUT"]}""");
        var page = SearchKit(RenderAs("admin"));
        Grid(page).Edit(0, "qty", "5");
        Grid(page).Select(0);

        page.Find("[data-testid=set-item-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=set-item-message]").TextContent.Should().Be(message));
        var data = page.FindComponent<QuroMultiGrid<SetComponentRow>>().Instance.Data;
        data.GetItem(data.CurrentRowKey!.Value).ItemCd.Should().Be("NUT");
        Grid(page).Column("qty")[0].Should().Be("5.00");
    }

    [Fact, Trait("Id", "SD-UT-506")]
    public void StaleRow_Shows409Message()
    {
        _api.SaveProblem = (HttpStatusCode.Conflict, """{"status":409,"code":"Common-E-004"}""");
        var page = SearchKit(RenderAs("admin"));
        Grid(page).Edit(0, "qty", "5");

        page.Find("[data-testid=set-item-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=set-item-message]").TextContent
            .Should().Be("다른 사용자가 먼저 수정했습니다. 다시 조회한 뒤 저장하세요."));
    }

    [Fact, Trait("Id", "SD-UT-508")]
    public void CopySet_AddsTheOtherSetsComponentsAsNewRows_AndTheSaveSendsThemAsAdded()
    {
        var page = SearchKit(RenderAs("admin"));

        page.Find("[data-testid=set-item-toolbar-copy-set]").Click();
        page.WaitForAssertion(() => page.LookupRows("set-item-set-lookup").Should().HaveCount(2));
        page.PickLookupRow<ItemDto, SetItem>("set-item-set-lookup", 1);

        page.WaitForAssertion(() => Grid(page).Column("itemCd").Should().Equal("BOLT", "NUT", "WASHER", "NUT"));
        Grid(page).Column("rowState").Should().Equal("", "", "신규", "신규");
        page.Find("[data-testid=set-item-toolbar-save]").Click();
        page.WaitForAssertion(() => _api.Saved.Should().ContainSingle());
        _api.Saved[0].Request.Added.Should().Equal(new SetComponentRowRequest(null, "WASHER", 3, null), new SetComponentRowRequest(null, "NUT", 4, null));
        _api.SetReads.Should().Equal("KIT01", "KIT02", "KIT01");
    }

    [Fact]
    public void CopySet_WaitsForASearchedSet()
    {
        var page = RenderAs("admin");

        page.Find("[data-testid=set-item-toolbar-copy-set]").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact, Trait("Id", "BC-UT-214")]
    public void UserWithoutFullAccess_CanOnlyRead_NewSaveAndCopyAreDisabledNotHidden()
    {
        var page = SearchKit(RenderAs("user"));

        page.Find("[data-testid=set-item-toolbar-new]").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=set-item-toolbar-save]").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=set-item-toolbar-copy-set]").HasAttribute("disabled").Should().BeTrue();
        Grid(page).IsReadOnly(0, "qty").Should().BeTrue();
        Grid(page).Edit(0, "qty", "9").Should().BeFalse();
        _api.Saved.Should().BeEmpty();
    }

    [Fact]
    public void English_ShowsTheEnglishTitleAndColumns()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("en");

        var page = RenderAs("admin");

        Grid(page).Headers.Select(h => h.Title).Should().ContainInOrder("Component", "Component Name", "Qty");
        page.Find("[data-testid=set-item-toolbar-copy-set]").TextContent.Should().Be("Copy Set");
        page.Markup.Should().Contain("Set Item Composition");
    }

    private static void Configure(HttpClient http) => http.BaseAddress = new Uri("http://api.test");

    private IRenderedComponent<SetItem> RenderAs(string role)
    {
        var store = Services.GetRequiredService<AccessTokenStore>();
        var sessionId = store.Add(new LoginResponse($"token-{role}", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", role, false, "ko"));
        _auth.SetAuthorized("kim");
        _auth.SetRoles(role);
        _auth.SetClaims(new Claim(WebAuthClaims.SessionId, sessionId));
        var page = Render<SetItem>();
        page.WaitForElement("[data-testid=set-item-search-item-cd]");
        return page;
    }

    private static IRenderedComponent<SetItem> SearchKit(IRenderedComponent<SetItem> page)
    {
        page.Find("[data-testid=set-item-search-item-cd]").Change("kit01");
        page.Find("[data-testid=set-item-toolbar-search]").Click();
        page.WaitForAssertion(() => Grid(page).Column("itemCd").Should().NotBeEmpty());
        return page;
    }

    private static IReadOnlyList<string> LookupTexts(IRenderedComponent<SetItem> page, string dialog) =>
        [.. page.LookupRows(dialog).Select(r => r.TextContent)];

    private static DataGridPage<SetComponentRow> Grid(IRenderedComponent<SetItem> page) =>
        page.DataGrid<SetComponentRow, SetItem>("set-item-grid");

    /// <summary>
    /// SD quantities with 2 decimals. Kit KIT01 (Gift kit: BOLT 2, NUT 1.5), Set KIT02 (WASHER 3, NUT 4) and KIT09 (not in use);
    /// components BOLT, LOTB (lot-managed), NUT (valid flag empty) and WASHER; FREIGHT (service) and OLDB (not in use) cannot be.
    /// </summary>
    private sealed class FakeApi : HttpMessageHandler
    {
        private static readonly ItemDto[] Items =
        [
            Item(1, "BOLT", "Bolt", "INVENTORY", "Y"),
            Item(2, "FREIGHT", "Freight", "SERVICE", "Y"),
            Item(3, "KIT01", "Gift kit", "KIT", "Y"),
            Item(4, "KIT02", "Spare kit", "SET", "Y"),
            Item(5, "KIT09", "Old kit", "SET", "N"),
            Item(6, "LOTB", "Lot bolt", "INVENTORY", "Y") with { LotManaged = true },
            Item(7, "NUT", "Nut", "INVENTORY", null),
            Item(8, "OLDB", "Old bolt", "INVENTORY", "N"),
            Item(9, "WASHER", "Washer", "INVENTORY", "Y"),
        ];

        private static readonly Dictionary<string, SetItemDto> Sets = new()
        {
            ["KIT01"] = new(3, "KIT01", "Gift kit", [Component(11, 1, "BOLT", "Bolt", 2), Component(12, 2, "NUT", "Nut", 1.5m)]),
            ["KIT02"] = new(4, "KIT02", "Spare kit", [Component(21, 1, "WASHER", "Washer", 3), Component(22, 2, "NUT", "Nut", 4)]),
        };

        public List<string> SetReads { get; } = [];

        public List<(string SetCd, BatchRequest<SetComponentRowRequest, SetComponentDeleteRequest> Request)> Saved { get; } = [];

        public (HttpStatusCode Status, string Body)? SaveProblem { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
            if (path == "/" + LocalizationRoutes.Base)
            {
                return Json(new LocalizationDto("US", "USD", "en", "America/New_York", "SALES_TAX", "MM/dd/yyyy", "DOCUMENT", [new("en", 1)], [], [],
                    [new("SD", 2, "HALF_UP")]));
            }
            if (path == "/" + ItemRoutes.Base)
            {
                var code = query["itemCd"];
                var name = query["itemName"];
                var found = Items.Where(i => (code is null || i.ItemCd.StartsWith(code, StringComparison.OrdinalIgnoreCase))
                                             && (name is null || i.ItemName!.Contains(name, StringComparison.OrdinalIgnoreCase))).ToList();
                return Json(new ItemListDto(found, found.Count));
            }
            if (path == $"/{SetItemRoutes.Base}/{SetItemRoutes.Batch}")
            {
                if (SaveProblem is { } problem)
                {
                    return new HttpResponseMessage(problem.Status) { Content = new StringContent(problem.Body, null, "application/problem+json") };
                }
                var body = await request.Content!.ReadFromJsonAsync<BatchRequest<SetComponentRowRequest, SetComponentDeleteRequest>>(cancellationToken);
                Saved.Add((query["itemCd"]!, body!));
                return Json(Sets[query["itemCd"]!]);
            }
            if (path != "/" + SetItemRoutes.Base)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            var setCd = query["itemCd"]!;
            SetReads.Add(setCd);
            return Sets.TryGetValue(setCd, out var set)
                ? Json(set)
                : new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
                {
                    Content = new StringContent($$"""{"status":422,"code":"SD-E-501","args":["{{setCd}}"]}""", null, "application/problem+json"),
                };
        }

        private static ItemDto Item(long id, string itemCd, string name, string type, string? validFlag) =>
            new(id, itemCd, name, null, null, type, "EA", "C1", validFlag, 0, 0, null, null, 0, null, null, null, null, null, null, false,
                "TAXABLE", "", 0, 0, 0, Read, "t", Read, "t");

        private static SetComponentDto Component(long id, int seq, string itemCd, string name, decimal qty) =>
            new(id, seq, 0, itemCd, name, "EA", qty, Read);

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
