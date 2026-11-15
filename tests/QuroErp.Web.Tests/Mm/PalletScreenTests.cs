using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Contracts.Mm.Pallets;
using QuroErp.Contracts.Mm.Warehouses;
using QuroErp.Web.Auth;
using QuroErp.Web.Bc.Items;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Pages.Mm;
using QuroErp.Web.Components.Pages.Mm.Models;
using QuroErp.Web.Components.Screens;
using QuroErp.Web.Mm.Pallets;
using QuroErp.Web.Mm.Warehouses;
using QuroErp.Web.Tests.Screens;

namespace QuroErp.Web.Tests.Mm;

/// <summary>Pallet screen /mm/pallet (MM-UC-035, TASK-MM-054) against a fake API.</summary>
public class PalletScreenTests : BunitContext
{
    private static readonly DateTimeOffset Read = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly FakeApi _api = new();
    private readonly BunitAuthorizationContext _auth;

    public PalletScreenTests()
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
        Services.AddHttpClient<PalletApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<ItemApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<WarehouseApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddScoped<IScreenAccess, ScreenAccess>();
        _auth = AddAuthorization();
    }

    [Fact, Trait("Rule", "MM-BR-911")]
    public void Open_ListsTheActivePallets_WithTheSpecColumns_AndTheStatusInTheUserLanguage()
    {
        var page = RenderAs("admin");
        var grid = Grid(page);

        grid.Headers.Select(h => h.Title).Should().ContainInOrder(
            "Pallet 번호", "품목 코드", "품목명", "Lot", "창고", "Location", "수량", "상태", "원 Pallet");
        grid.Column("palletNo").Should().Equal("PL2611000001", "PL2611000002", "PL2611000003");
        grid.Text(0, "statusName").Should().Be("사용 중");
        grid.Text(1, "parentPalletNo").Should().Be("PL2611000001");
        _api.Queries.Should().Equal("?status=ACTIVE");
        page.Field("pallet-search-status").Value.Should().Be("ACTIVE");
    }

    [Fact]
    public void SearchConditions_AreSentInUpperCase_AndAnEmptyStatusListsClosedPallets()
    {
        var page = RenderAs("admin");

        page.Field("pallet-search-pallet-no").Change("pl2611");
        page.Field("pallet-search-warehouse").Change("1");
        page.Field("pallet-search-status").Change(null);
        page.Find("[data-testid=pallet-toolbar-search]").Click();

        page.WaitForAssertion(() => Grid(page).Rows.Should().HaveCount(4));
        _api.Queries[^1].Should().Be("?palletNo=PL2611&warehouseId=1");
        Grid(page).Text(3, "statusName").Should().Be("종료");
    }

    [Fact, Trait("Id", "MM-UC-035-T04"), Trait("Rule", "MM-BR-916")]
    public void TheHistory_FollowsTheCurrentRow()
    {
        var page = RenderAs("admin");

        Grid(page).Select(1);

        page.WaitForAssertion(() => page.FindAll("[data-testid=pallet-history-row]").Should().HaveCount(2));
        var cells = page.FindAll("[data-testid=pallet-history-row]")[1].QuerySelectorAll("td").Select(c => c.TextContent.Trim());
        cells.Should().Equal("분할", new DateOnly(2026, 11, 2).ToString("d", CultureInfo.CurrentCulture), "4", "PL2611000001", "");
        page.Find("[data-testid=pallet-history] h2").TextContent.Should().Contain("PL2611000002");
    }

    [Fact, Trait("Id", "MM-UC-035-T01"), Trait("Rule", "MM-BR-914")]
    public void Split_SendsTheQtyAndTheReadTime_ThenSearchesAgainWithTheNewNumber()
    {
        var page = RenderAs("admin");
        Grid(page).Select(0);

        page.Find("[data-testid=pallet-toolbar-split]").Click();
        page.Find("[data-testid=pallet-split-qty]").Change("1.5");
        page.Find("[data-testid=pallet-split-ok]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=pallet-message]").TextContent
            .Should().Be("Pallet PL2611000001에서 새 Pallet PL2611000009을(를) 나눴습니다."));
        _api.Splits.Should().Equal((1L, new SplitPalletRequest(1.5m, Read)));
        _api.Queries.Should().HaveCount(2);
        page.FindAll("[data-testid=pallet-split]").Should().BeEmpty();
    }

    [Theory, Trait("Rule", "MM-BR-914")]
    [InlineData(HttpStatusCode.UnprocessableEntity, """["PL2611000001",4]""", "MM-E-914",
        "Pallet PL2611000001을(를) 나눌 수 없습니다. ACTIVE Pallet이어야 하고, 나눌 수량은 0보다 크고 Pallet 수량 4보다 작아야 합니다.")]
    [InlineData(HttpStatusCode.Conflict, "[]", "Common-E-004", "다른 사용자가 먼저 수정했습니다. 다시 조회한 뒤 저장하세요.")]
    public void RefusedSplit_KeepsTheDialogOpen_WithTheApiMessage(HttpStatusCode status, string args, string code, string message)
    {
        _api.Problem = (status, $$"""{"status":{{(int)status}},"code":"{{code}}","args":{{args}}}""");
        var page = RenderAs("admin");
        Grid(page).Select(0);

        page.Find("[data-testid=pallet-toolbar-split]").Click();
        page.Find("[data-testid=pallet-split-qty]").Change("4");
        page.Find("[data-testid=pallet-split-ok]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=pallet-split-message]").TextContent.Should().Be(message));
        page.FindAll("[data-testid=pallet-split]").Should().ContainSingle();
        _api.Queries.Should().ContainSingle("nothing changed, so the grid is not searched again");
    }

    [Fact, Trait("Id", "MM-UC-035-T02"), Trait("Id", "MM-UC-035-T03"), Trait("Rule", "MM-BR-915")]
    public void Merge_OffersOnlyActivePalletsOfTheSameKey_AndSendsTheCheckedOnes()
    {
        var page = RenderAs("admin");
        Grid(page).Select(0);

        page.Find("[data-testid=pallet-toolbar-merge]").Click();
        page.FindAll("[data-testid=pallet-merge-row]").Select(r => r.QuerySelectorAll("td")[1].TextContent)
            .Should().Equal(["PL2611000002"], "PL2611000003 is on another Lot");
        page.Find("[data-testid=pallet-merge-ok]").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=pallet-merge-check-2]").Change(true);
        page.Find("[data-testid=pallet-merge-ok]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=pallet-message]").TextContent.Should().Be("Pallet PL2611000001(으)로 합쳤습니다. 수량 8"));
        _api.Merges.Should().ContainSingle().Which.Should().BeEquivalentTo(new MergePalletsRequest(1, [2]));
    }

    [Fact, Trait("Rule", "MM-BR-911")]
    public void SplitAndMerge_AreDisabled_OnAClosedRow()
    {
        var page = RenderAs("admin");
        page.Field("pallet-search-status").Change(null);
        page.Find("[data-testid=pallet-toolbar-search]").Click();
        page.WaitForAssertion(() => Grid(page).Rows.Should().HaveCount(4));

        Grid(page).Select(3);

        page.Find("[data-testid=pallet-toolbar-split]").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=pallet-toolbar-merge]").HasAttribute("disabled").Should().BeTrue();
        Grid(page).Select(0);
        page.Find("[data-testid=pallet-toolbar-split]").HasAttribute("disabled").Should().BeFalse();
    }

    // security.md "화면 쓰기 권한": the buttons follow IScreenAccess for this screen path
    [Fact, Trait("Id", "BC-UT-214")]
    public void UserWithoutWriteAccess_CanOnlyRead()
    {
        var access = new FakeScreenAccess(false);
        Services.AddSingleton<IScreenAccess>(access);

        var page = RenderAs("admin");
        Grid(page).Select(0);

        page.Find("[data-testid=pallet-toolbar-split]").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=pallet-toolbar-merge]").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=pallet-toolbar-search]").HasAttribute("disabled").Should().BeFalse();
        access.Asked.Should().Equal("/mm/pallet");
    }

    [Fact]
    public void English_ShowsTheEnglishLabels()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("en");

        var page = RenderAs("admin");

        Grid(page).Headers.Select(h => h.Title).Should().Contain(["Pallet No.", "Split From", "Status"]);
        Grid(page).Text(0, "statusName").Should().Be("Active");
        page.Find("[data-testid=pallet-toolbar-split]").TextContent.Should().Be("Split");
    }

    [Fact]
    public void EveryInputButtonAndGrid_HasTestIdByRule()
    {
        var page = RenderAs("admin");
        Grid(page).Select(0);
        page.Find("[data-testid=pallet-toolbar-merge]").Click();

        var elements = page.FindAll("input, select, textarea, button:not([hidden]), .quro-grid");
        elements.Should().NotBeEmpty();
        elements.Select(DataGridPageExtensions.TestIdOf).Should()
            .OnlyContain(id => id != null && System.Text.RegularExpressions.Regex.IsMatch(id, "^pallet(-(search|grid|toolbar|message|history|split|merge)(-[a-z0-9]+)*)?$"));
    }

    private static void Configure(HttpClient http) => http.BaseAddress = new Uri("http://api.test");

    private IRenderedComponent<Pallet> RenderAs(string role)
    {
        var store = Services.GetRequiredService<AccessTokenStore>();
        var sessionId = store.Add(new LoginResponse($"token-{role}", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", role, false, "ko"));
        _auth.SetAuthorized("kim");
        _auth.SetRoles(role);
        _auth.SetClaims(new Claim(WebAuthClaims.SessionId, sessionId));
        var page = Render<Pallet>();
        page.WaitForAssertion(() => Grid(page).Rows.Should().NotBeEmpty());
        return page;
    }

    private static DataGridPage<PalletRow> Grid(IRenderedComponent<Pallet> page) => page.DataGrid<PalletRow, Pallet>("pallet-grid");

    /// <summary>
    /// Warehouse MAIN (1). Item BOLT on MAIN '*': PL2611000001 (4, lot L1), PL2611000002 (4, lot L1, split from 01),
    /// PL2611000003 (3, lot L2) and PL2611000004 (CLOSED, lot L1).
    /// </summary>
    private sealed class FakeApi : HttpMessageHandler
    {
        private static readonly PalletDto[] Pallets =
        [
            Pallet(1, 4m, 20, "L1", "ACTIVE", null),
            Pallet(2, 4m, 20, "L1", "ACTIVE", "PL2611000001"),
            Pallet(3, 3m, 21, "L2", "ACTIVE", null),
            Pallet(4, 0m, 20, "L1", "CLOSED", null),
        ];

        public List<string> Queries { get; } = [];

        public List<(long Id, SplitPalletRequest Request)> Splits { get; } = [];

        public List<MergePalletsRequest> Merges { get; } = [];

        public (HttpStatusCode Status, string Body)? Problem { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/" + WarehouseRoutes.Options)
            {
                return Json(new List<WarehouseOptionDto> { new(1, "MAIN", "Main", "NORMAL", true) });
            }
            if (request.Method == HttpMethod.Post && Problem is { } problem)
            {
                return new HttpResponseMessage(problem.Status) { Content = new StringContent(problem.Body, null, "application/problem+json") };
            }
            if (path == "/" + PalletRoutes.Merge && request.Method == HttpMethod.Post)
            {
                Merges.Add((await request.Content!.ReadFromJsonAsync<MergePalletsRequest>(cancellationToken))!);
                return Json(Pallets[0] with { Qty = 8m });
            }
            if (path.EndsWith("/split", StringComparison.Ordinal) && request.Method == HttpMethod.Post)
            {
                var id = long.Parse(path.Split('/')[^2], CultureInfo.InvariantCulture);
                Splits.Add((id, (await request.Content!.ReadFromJsonAsync<SplitPalletRequest>(cancellationToken))!));
                return new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(new SplitPalletResultDto(9, "PL2611000009")) };
            }
            if (path == "/" + PalletRoutes.Base)
            {
                var query = request.RequestUri.Query;
                Queries.Add(query);
                var status = System.Web.HttpUtility.ParseQueryString(query)["status"];
                return Json(Pallets.Where(p => status is null || p.Status == status).ToList());
            }
            if (path.StartsWith("/" + PalletRoutes.Base + "/", StringComparison.Ordinal))
            {
                var id = long.Parse(path.Split('/')[^1], CultureInfo.InvariantCulture);
                return Json(Pallets.Single(p => p.Id == id) with
                {
                    Events =
                    [
                        new PalletEventDto("CREATE", new DateOnly(2026, 11, 1), 8m, null, "GR2611000001"),
                        new PalletEventDto("SPLIT", new DateOnly(2026, 11, 2), 4m, "PL2611000001", null),
                    ],
                });
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static PalletDto Pallet(long id, decimal qty, long lotId, string lotNo, string status, string? parent) =>
            new(id, $"PL261100000{id}", 10, "BOLT", "Hex Bolt", lotId, lotNo, 1, "MAIN", 5, "*", qty, status, parent, Read);

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
