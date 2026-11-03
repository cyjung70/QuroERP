using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuroErp.Contracts.Common;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Contracts.Mm.Warehouses;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Pages.Mm;
using QuroErp.Web.Components.Pages.Mm.Models;
using QuroErp.Web.Components.Screens;
using QuroErp.Web.Mm.Warehouses;
using QuroErp.Web.Tests.Screens;

namespace QuroErp.Web.Tests.Mm;

/// <summary>Warehouse screen /mm/warehouse (MM-UC-017, TASK-MM-026) against a fake warehouse API.</summary>
public class WarehouseScreenTests : BunitContext
{
    private static readonly DateTimeOffset Read = new(2026, 10, 30, 0, 0, 0, TimeSpan.Zero);

    private readonly FakeWarehouseApi _api = new();
    private readonly BunitAuthorizationContext _auth;

    public WarehouseScreenTests()
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
        Services.AddHttpClient<WarehouseApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        _auth = AddAuthorization();
    }

    [Fact, Trait("Id", "MM-UC-017-T01")]
    public void Open_ListsWarehousesInUse_WithTheSpecColumns()
    {
        var grid = Grid(RenderAs("admin"));

        grid.Headers.Select(h => h.Title).Should().ContainInOrder("창고 코드", "창고 이름", "기본 창고", "사용", "창고 유형", "비고");
        grid.Column("warehouseCode").Should().Equal("CONSIGN", "MAIN", "W02");
        grid.Column("warehouseType").Should().Equal("CONSIGNMENT", "NORMAL", "NORMAL");
        new[] { grid.IsChecked(0, "isDefault"), grid.IsChecked(1, "isDefault"), grid.IsChecked(2, "isDefault") }.Should().Equal(false, true, false);
        _api.Queries.Should().Equal("");
    }

    [Fact, Trait("Id", "MM-UC-017-T10")]
    public void IncludeInactive_SearchesAgain_WithWarehousesNotInUse()
    {
        var page = RenderAs("admin");

        page.Find("[data-testid=warehouse-search-include-inactive]").Change(true);
        page.Find("[data-testid=warehouse-toolbar-search]").Click();

        page.WaitForAssertion(() => Grid(page).Column("warehouseCode").Should().Equal("CONSIGN", "MAIN", "OLD", "W02"));
        Grid(page).IsChecked(2, "useYn").Should().BeFalse();
        _api.Queries.Should().Equal("", "?includeInactive=true");
    }

    [Fact, Trait("Id", "MM-UC-017-T03"), Trait("Id", "MM-UT-701"), Trait("Id", "MM-UT-709")]
    public void Code_IsEnteredOnNewRowsOnly_AndTheTypeNever()
    {
        var page = RenderAs("admin");
        var grid = Grid(page);

        page.Find("[data-testid=warehouse-toolbar-new]").Click();

        grid.IsReadOnly(1, "warehouseCode").Should().BeTrue();
        grid.IsReadOnly(3, "warehouseCode").Should().BeFalse();
        grid.IsReadOnly(3, "warehouseType").Should().BeTrue();
        grid.IsChecked(3, "useYn").Should().BeTrue();
        grid.IsChecked(3, "isDefault").Should().BeFalse();
    }

    [Fact, Trait("Id", "MM-UC-017-T04"), Trait("Id", "MM-UT-702"), Trait("Rule", "MM-BR-702")]
    public void CheckingADefault_UnchecksTheOtherRows()
    {
        var page = RenderAs("admin");
        var grid = Grid(page);

        grid.Toggle(2, "isDefault").Should().BeTrue();

        page.WaitForAssertion(() =>
            new[] { grid.IsChecked(0, "isDefault"), grid.IsChecked(1, "isDefault"), grid.IsChecked(2, "isDefault") }.Should().Equal(false, false, true));
    }

    [Fact, Trait("Id", "MM-UC-017-T01"), Trait("Id", "MM-UC-017-T04")]
    public void AddChangeAndDelete_AreSavedTogether_WithTheCodeInUpperCase()
    {
        var page = RenderAs("admin");
        var grid = Grid(page);

        grid.Toggle(2, "isDefault").Should().BeTrue();
        grid.Edit(2, "remarks", "Moved").Should().BeTrue();
        page.Find("[data-testid=warehouse-toolbar-new]").Click();
        grid.Edit(3, "warehouseCode", "east").Should().BeTrue();
        grid.Edit(3, "warehouseName", "East").Should().BeTrue();

        page.Find("[data-testid=warehouse-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=warehouse-message]").TextContent.Should().Be("저장했습니다."));
        var saved = _api.Saved.Should().ContainSingle().Subject;
        saved.Added.Should().Equal(new WarehouseSaveRow(null, "EAST", "East", false, true, null, null, null));
        saved.Updated.Should().BeEquivalentTo(
        [
            new WarehouseSaveRow(1, "MAIN", "Main", false, true, "NORMAL", null, Read),
            new WarehouseSaveRow(2, "W02", "Second", true, true, "NORMAL", "Moved", Read),
        ]);
        saved.Deleted.Should().BeEmpty();
    }

    [Fact, Trait("Id", "MM-UC-017-T07")]
    public void DeletedRow_IsSentWithItsUpdatedAt()
    {
        var page = RenderAs("admin");
        var grid = Grid(page);

        grid.Click(2, "deleteRow");
        page.Find("[data-testid=warehouse-toolbar-save]").Click();

        page.WaitForAssertion(() => _api.Saved.Should().ContainSingle().Which.Deleted.Should().Equal(new WarehouseKey(2, Read)));
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, """["W02","ONHAND"]""", "MM-E-704", "창고 W02은(는) 사용 안 함으로 바꾸거나 지울 수 없습니다 (사유: ONHAND).")]
    [InlineData(HttpStatusCode.Conflict, "[]", "Common-E-004", "다른 사용자가 먼저 수정했습니다. 다시 조회한 뒤 저장하세요.")]
    [Trait("Id", "MM-UC-017-T05"), Trait("Id", "MM-UT-704")]
    public void RejectedSave_ShowsTheApiMessage_AndTheGridKeepsTheChanges(HttpStatusCode status, string args, string code, string message)
    {
        _api.SaveProblem = (status, $$"""{"status":{{(int)status}},"code":"{{code}}","args":{{args}}}""");
        var page = RenderAs("admin");
        var grid = Grid(page);
        grid.Toggle(2, "useYn").Should().BeTrue();

        page.Find("[data-testid=warehouse-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=warehouse-message]").TextContent.Should().Be(message));
        grid.IsChecked(2, "useYn").Should().BeFalse();
    }

    [Fact, Trait("Id", "BC-UT-214")]
    public void UserWithoutFullAccess_CanOnlyRead_NewAndSaveAreDisabledNotHidden()
    {
        var page = RenderAs("user");
        var grid = Grid(page);

        page.Find("[data-testid=warehouse-toolbar-new]").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=warehouse-toolbar-save]").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=warehouse-toolbar-search]").HasAttribute("disabled").Should().BeFalse();
        grid.IsReadOnly(1, "warehouseName").Should().BeTrue();
        grid.Toggle(2, "isDefault").Should().BeFalse();
        _api.Saved.Should().BeEmpty();
    }

    [Fact]
    public void EveryInputButtonAndGrid_HasTestIdByRule()
    {
        var page = RenderAs("admin");

        var elements = page.FindAll("input, select, textarea, button:not([hidden]), .quro-grid");
        elements.Should().NotBeEmpty();
        elements.Select(DataGridPageExtensions.TestIdOf).Should()
            .OnlyContain(id => id != null && System.Text.RegularExpressions.Regex.IsMatch(id, "^warehouse(-(search|grid|toolbar|message)(-[a-z0-9]+)*)?$"));
    }

    [Fact]
    public void ToRequest_LeavesTheTypeOfANewRowToTheApi()
    {
        var row = new WarehouseRow { WarehouseCode = "W09", WarehouseName = "Nine", WarehouseType = "CONSIGNMENT" };

        var request = WarehouseRow.ToRequest(new BatchChanges<WarehouseRow>([row], [], []));

        request.Added.Should().ContainSingle().Which.WarehouseType.Should().BeNull();
    }

    private static void Configure(HttpClient http) => http.BaseAddress = new Uri("http://api.test");

    private IRenderedComponent<Warehouse> RenderAs(string role)
    {
        var store = Services.GetRequiredService<AccessTokenStore>();
        var sessionId = store.Add(new LoginResponse($"token-{role}", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", role, false, "ko"));
        _auth.SetAuthorized("kim");
        _auth.SetRoles(role);
        _auth.SetClaims(new Claim(WebAuthClaims.SessionId, sessionId));
        var page = Render<Warehouse>();
        page.WaitForAssertion(() => Grid(page).Rows.Should().NotBeEmpty());
        return page;
    }

    private static DataGridPage<WarehouseRow> Grid(IRenderedComponent<Warehouse> page) => page.DataGrid<WarehouseRow, Warehouse>("warehouse-grid");

    /// <summary>CONSIGN (consignment), MAIN (the default), W02 and OLD (not in use), in code order.</summary>
    private sealed class FakeWarehouseApi : HttpMessageHandler
    {
        private static readonly WarehouseDto[] Warehouses =
        [
            new(3, "CONSIGN", "Consignment", false, true, "CONSIGNMENT", null, Read),
            new(1, "MAIN", "Main", true, true, "NORMAL", null, Read),
            new(4, "OLD", "Closed", false, false, "NORMAL", null, Read),
            new(2, "W02", "Second", false, true, "NORMAL", null, Read),
        ];

        public List<string> Queries { get; } = [];

        public List<BatchRequest<WarehouseSaveRow, WarehouseKey>> Saved { get; } = [];

        public (HttpStatusCode Status, string Body)? SaveProblem { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/" + WarehouseRoutes.Batch && request.Method == HttpMethod.Post)
            {
                if (SaveProblem is { } problem)
                {
                    return new HttpResponseMessage(problem.Status) { Content = new StringContent(problem.Body, null, "application/problem+json") };
                }
                Saved.Add((await request.Content!.ReadFromJsonAsync<BatchRequest<WarehouseSaveRow, WarehouseKey>>(cancellationToken))!);
                return Json(Warehouses.Where(w => w.UseYn).ToList());
            }
            if (path != "/" + WarehouseRoutes.Base || request.Method != HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            var query = request.RequestUri.Query;
            Queries.Add(query);
            return Json(Warehouses.Where(w => w.UseYn || query.Contains("includeInactive=true", StringComparison.Ordinal)).ToList());
        }

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
