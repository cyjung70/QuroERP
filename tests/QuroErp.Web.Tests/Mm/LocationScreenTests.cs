using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuroErp.Contracts.Bc.UserProfiles;
using QuroErp.Contracts.Common;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Contracts.Mm.Locations;
using QuroErp.Contracts.Mm.Warehouses;
using QuroErp.Web.Auth;
using QuroErp.Web.Bc.UserProfiles;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Pages.Mm;
using QuroErp.Web.Components.Pages.Mm.Models;
using QuroErp.Web.Components.Screens;
using QuroErp.Web.Mm.Locations;
using QuroErp.Web.Mm.Warehouses;
using QuroErp.Web.Tests.Screens;

namespace QuroErp.Web.Tests.Mm;

/// <summary>Location screen /mm/location (MM-UC-034, TASK-MM-052) against a fake API.</summary>
public class LocationScreenTests : BunitContext
{
    private static readonly DateTimeOffset Read = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly FakeApi _api = new();
    private readonly BunitAuthorizationContext _auth;

    public LocationScreenTests()
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
        Services.AddHttpClient<LocationApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<WarehouseApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<UserProfileApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        // Screens ask IScreenAccess, which reads the role from this test authorization (security.md "화면 쓰기 권한")
        Services.AddScoped<IScreenAccess, ScreenAccess>();
        _auth = AddAuthorization();
    }

    [Fact, Trait("Id", "MM-UC-034-T01"), Trait("Rule", "MM-BR-712")]
    public void Open_ListsTheUserDefaultWarehouse_StarFirst_WithTheSpecColumns()
    {
        var page = RenderAs("admin");
        var grid = Grid(page);

        grid.Headers.Select(h => h.Title).Should().ContainInOrder("Location 코드", "Location 이름", "사용", "현재고", "비고");
        grid.Column("locationCode").Should().Equal("*", "A-01", "B-01");
        grid.IsChecked(2, "useYn").Should().BeFalse("Locations not in use stay listed so they can be put back in use");
        grid.Text(1, "onhandQty").Should().Be("5");
        _api.Queries.Should().Equal("?warehouseId=2&includeInactive=true");
        page.Field("location-search-warehouse").Options.Select(o => o.Text).Should().Equal("MAIN Main", "W02 Second");
    }

    [Fact, Trait("Id", "MM-UC-034-T01")]
    public void AnotherWarehouse_IsSearchedWithTheSearchButton()
    {
        var page = RenderAs("admin");

        page.Field("location-search-warehouse").Change("1");
        page.Find("[data-testid=location-toolbar-search]").Click();

        page.WaitForAssertion(() => Grid(page).Column("locationCode").Should().Equal("*", "M-01"));
        _api.Queries.Should().Equal("?warehouseId=2&includeInactive=true", "?warehouseId=1&includeInactive=true");
    }

    [Fact, Trait("Id", "MM-UC-034-T04"), Trait("Rule", "MM-BR-712")]
    public void TheStarRow_IsLocked_AndCannotBeMarkedDeleted()
    {
        var page = RenderAs("admin");
        var grid = Grid(page);

        grid.IsReadOnly(0, "locationName").Should().BeTrue();
        grid.IsReadOnly(0, "useYn").Should().BeTrue();
        grid.IsReadOnly(1, "locationName").Should().BeFalse();
        grid.IsReadOnly(1, "locationCode").Should().BeTrue("the code is entered on new rows only");
        grid.Toggle(0, "useYn").Should().BeFalse();

        grid.Select(0);
        page.Find("[data-testid=location-toolbar-mark-delete]").HasAttribute("disabled").Should().BeTrue();
        grid.Select(1);
        page.Find("[data-testid=location-toolbar-mark-delete]").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact, Trait("Id", "MM-UC-034-T02"), Trait("Rule", "MM-BR-712")]
    public void AddChangeAndDelete_AreSavedTogether_UnderTheReadWarehouse_WithTheCodeInUpperCase()
    {
        var page = RenderAs("admin");
        var grid = Grid(page);

        grid.Edit(1, "remarks", "Front").Should().BeTrue();
        grid.Select(2);
        page.Find("[data-testid=location-toolbar-mark-delete]").Click();
        page.Find("[data-testid=location-toolbar-add-row]").Click();
        grid.IsReadOnly(3, "locationCode").Should().BeFalse();
        grid.Edit(3, "locationCode", "c-03").Should().BeTrue();
        grid.Edit(3, "locationName", "Shelf C3").Should().BeTrue();

        page.Find("[data-testid=location-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=location-message]").TextContent.Should().Be("저장했습니다."));
        var saved = _api.Saved.Should().ContainSingle().Subject;
        saved.Added.Should().Equal(new LocationSaveRow(null, 2, "C-03", "Shelf C3", true, null, null));
        saved.Updated.Should().Equal(new LocationSaveRow(21, 2, "A-01", "Shelf A1", true, "Front", Read));
        saved.Deleted.Should().Equal(new LocationKey(22, Read));
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, """["A-01","ONHAND"]""", "MM-E-717", "Location A-01은(는) 사용 안 함으로 바꾸거나 지울 수 없습니다 (사유: ONHAND).")]
    [InlineData(HttpStatusCode.Conflict, "[]", "Common-E-004", "다른 사용자가 먼저 수정했습니다. 다시 조회한 뒤 저장하세요.")]
    [Trait("Id", "MM-UC-034-T05"), Trait("Rule", "MM-BR-717")]
    public void RejectedSave_ShowsTheApiMessage_AndTheGridKeepsTheChanges(HttpStatusCode status, string args, string code, string message)
    {
        _api.SaveProblem = (status, $$"""{"status":{{(int)status}},"code":"{{code}}","args":{{args}}}""");
        var page = RenderAs("admin");
        var grid = Grid(page);
        grid.Toggle(1, "useYn").Should().BeTrue();

        page.Find("[data-testid=location-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=location-message]").TextContent.Should().Be(message));
        grid.IsChecked(1, "useYn").Should().BeFalse();
    }

    // security.md "화면 쓰기 권한": the buttons follow IScreenAccess for this screen path
    [Fact, Trait("Id", "BC-UT-214")]
    public void UserWithoutWriteAccess_CanOnlyRead()
    {
        var access = new FakeScreenAccess(false);
        Services.AddSingleton<IScreenAccess>(access);

        var page = RenderAs("admin");

        page.Find("[data-testid=location-toolbar-new]").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=location-toolbar-save]").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=location-toolbar-search]").HasAttribute("disabled").Should().BeFalse();
        Grid(page).IsReadOnly(1, "locationName").Should().BeTrue();
        access.Asked.Should().Equal("/mm/location");
    }

    [Fact]
    public void EveryInputButtonAndGrid_HasTestIdByRule()
    {
        var page = RenderAs("admin");

        var elements = page.FindAll("input, select, textarea, button:not([hidden]), .quro-grid");
        elements.Should().NotBeEmpty();
        elements.Select(DataGridPageExtensions.TestIdOf).Should()
            .OnlyContain(id => id != null && System.Text.RegularExpressions.Regex.IsMatch(id, "^location(-(search|grid|toolbar|message)(-[a-z0-9]+)*)?$"));
    }

    private static void Configure(HttpClient http) => http.BaseAddress = new Uri("http://api.test");

    private IRenderedComponent<Location> RenderAs(string role)
    {
        var store = Services.GetRequiredService<AccessTokenStore>();
        var sessionId = store.Add(new LoginResponse($"token-{role}", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", role, false, "ko"));
        _auth.SetAuthorized("kim");
        _auth.SetRoles(role);
        _auth.SetClaims(new Claim(WebAuthClaims.SessionId, sessionId));
        var page = Render<Location>();
        page.WaitForAssertion(() => Grid(page).Rows.Should().NotBeEmpty());
        return page;
    }

    private static DataGridPage<LocationRow> Grid(IRenderedComponent<Location> page) => page.DataGrid<LocationRow, Location>("location-grid");

    /// <summary>
    /// Warehouses MAIN (1), W02 (2, the user's default) and the consignment warehouse; W02 has '*', A-01 with a balance of 5 and
    /// B-01 not in use, MAIN has '*' and M-01.
    /// </summary>
    private sealed class FakeApi : HttpMessageHandler
    {
        private static readonly LocationDto[] Locations =
        [
            new(10, 1, "*", null, true, true, null, 0, Read),
            new(11, 1, "M-01", null, false, true, null, 0, Read),
            new(20, 2, "*", null, true, true, null, 0, Read),
            new(21, 2, "A-01", "Shelf A1", false, true, null, 5, Read),
            new(22, 2, "B-01", null, false, false, null, 0, Read),
        ];

        public List<string> Queries { get; } = [];

        public List<BatchRequest<LocationSaveRow, LocationKey>> Saved { get; } = [];

        public (HttpStatusCode Status, string Body)? SaveProblem { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/" + WarehouseRoutes.Options)
            {
                return Json(new List<WarehouseOptionDto>
                {
                    new(1, "MAIN", "Main", "NORMAL", true), new(2, "W02", "Second", "NORMAL", true), new(3, "CONSIGN", "Consignment", "CONSIGNMENT", true),
                });
            }
            if (path == "/" + UserProfileRoutes.Base)
            {
                return Json(new UserProfileDto("kim", "Kim", null, null, "ko", 2, "W02", "Second", Read));
            }
            if (path == "/" + LocationRoutes.Batch && request.Method == HttpMethod.Post)
            {
                if (SaveProblem is { } problem)
                {
                    return new HttpResponseMessage(problem.Status) { Content = new StringContent(problem.Body, null, "application/problem+json") };
                }
                Saved.Add((await request.Content!.ReadFromJsonAsync<BatchRequest<LocationSaveRow, LocationKey>>(cancellationToken))!);
                return Json(Locations.Where(l => l.WarehouseId == 2).ToList());
            }
            if (path != "/" + LocationRoutes.Base || request.Method != HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            var query = request.RequestUri.Query;
            Queries.Add(query);
            var warehouseId = long.Parse(System.Web.HttpUtility.ParseQueryString(query)["warehouseId"]!, CultureInfo.InvariantCulture);
            return Json(Locations.Where(l => l.WarehouseId == warehouseId).ToList());
        }

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
