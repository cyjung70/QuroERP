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
using QuroErp.Contracts.Mm.Labels;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Pages.Mm;
using QuroErp.Web.Components.Pages.Mm.Models;
using QuroErp.Web.Components.Screens;
using QuroErp.Web.Mm.Labels;
using QuroErp.Web.Tests.Screens;

namespace QuroErp.Web.Tests.Mm;

/// <summary>Label format screen /mm/label-format (MM-UC-036, TASK-MM-056) against a fake API.</summary>
public class LabelFormatScreenTests : BunitContext
{
    private static readonly DateTimeOffset Read = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly string[] ItemFieldsReordered = ["itemName", "itemCd"];
    private static readonly string[] LocationFields = ["warehouseCode"];

    private readonly FakeApi _api = new();
    private readonly BunitAuthorizationContext _auth;

    public LabelFormatScreenTests()
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
        Services.AddHttpClient<LabelFormatApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddScoped<IScreenAccess, ScreenAccess>();
        _auth = AddAuthorization();
    }

    [Fact, Trait("Id", "MM-UC-036-T01"), Trait("Rule", "MM-BR-917")]
    public void Open_ListsTheFourFormats_WithTheSpecColumns_AndTheFieldNamesInTheUserLanguage()
    {
        var page = RenderAs();
        var grid = Grid(page);

        grid.Headers.Select(h => h.Title).Should().ContainInOrder(
            "라벨 종류", "출력 형식", "너비(mm)", "높이(mm)", "해상도(dpi)", "찍을 항목", "사용");
        grid.Column("labelType").Should().Equal("PALLET", "ITEM", "LOT", "LOCATION");
        grid.Text(1, "fieldNames").Should().Be("품목 코드, 품목명");
        grid.IsReadOnly(0, "labelType").Should().BeTrue();
        grid.IsReadOnly(0, "fieldNames").Should().BeTrue();
        grid.IsReadOnly(0, "widthMm").Should().BeFalse();
    }

    [Fact, Trait("Rule", "MM-BR-917")]
    public void RowsCannotBeAddedOrDeleted()
    {
        var page = RenderAs();
        Grid(page).Select(0);

        page.Find("[data-testid=label-format-toolbar-add-row]").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=label-format-toolbar-mark-delete]").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=label-format-toolbar-save]").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact, Trait("Rule", "MM-BR-917")]
    public void ChangedRow_IsSaved_WithItsValuesAndTheReadTime()
    {
        var page = RenderAs();
        var grid = Grid(page);

        grid.Edit(1, "widthMm", "60").Should().BeTrue();
        page.Find("[data-testid=label-format-toolbar-save]").Click();

        page.WaitForAssertion(() => _api.Saves.Should().ContainSingle());
        var save = _api.Saves[0];
        save.Added.Should().BeEmpty();
        save.Deleted.Should().BeEmpty();
        save.Updated.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new LabelFormatSaveRow("ITEM", "ZPL", 60, 25, 203, ["itemCd", "itemName"], true, Read));
        page.WaitForAssertion(() => _api.Lists.Should().Be(2, "the grid is read again after the save"));
    }

    [Fact, Trait("Rule", "MM-BR-917")]
    public void FieldDialog_ReordersAndUnchecks_ThenTheRowIsSavedInTheNewOrder()
    {
        var page = RenderAs();
        var grid = Grid(page);
        grid.Select(3);

        page.Find("[data-testid=label-format-toolbar-fields]").Click();
        page.FindAll("[data-testid=label-format-fields-row]").Select(r => r.QuerySelectorAll("td")[1].TextContent)
            .Should().Equal("창고 코드", "Location 코드");
        page.Find("[data-testid=label-format-fields-check-locationcode]").Change(false);
        page.Find("[data-testid=label-format-fields-ok]").Click();

        page.FindAll("[data-testid=label-format-fields]").Should().BeEmpty();
        grid.Text(3, "fieldNames").Should().Be("창고 코드");
        grid.Select(1);
        page.Find("[data-testid=label-format-toolbar-fields]").Click();
        page.Find("[data-testid=label-format-fields-down-itemcd]").Click();
        page.Find("[data-testid=label-format-fields-ok]").Click();
        page.Find("[data-testid=label-format-toolbar-save]").Click();

        page.WaitForAssertion(() => _api.Saves.Should().ContainSingle());
        _api.Saves[0].Updated.Select(r => (r.LabelType, (IEnumerable<string>)r.Fields!)).Should().BeEquivalentTo(
            [("ITEM", (IEnumerable<string>)ItemFieldsReordered), ("LOCATION", LocationFields)]);
    }

    [Fact, Trait("Rule", "MM-BR-917")]
    public void FieldDialog_CannotApplyNoFields_AndCloseKeepsTheRow()
    {
        var page = RenderAs();
        Grid(page).Select(1);

        page.Find("[data-testid=label-format-toolbar-fields]").Click();
        page.Find("[data-testid=label-format-fields-check-itemcd]").Change(false);
        page.Find("[data-testid=label-format-fields-check-itemname]").Change(false);

        page.Find("[data-testid=label-format-fields-ok]").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=label-format-fields-close]").Click();
        Grid(page).Text(1, "fieldNames").Should().Be("품목 코드, 품목명");
    }

    [Theory, Trait("Id", "MM-UC-036-T02"), Trait("Rule", "MM-BR-917")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "MM-E-917", """["ITEM"]""",
        "ITEM 라벨 형식을 저장할 수 없습니다. 출력 형식은 ZPL 또는 PDF, 너비·높이는 0보다 크고, dpi는 203 또는 300이며, 항목은 그 라벨 종류의 항목을 하나 이상, 한 번씩 골라야 합니다.")]
    [InlineData(HttpStatusCode.Conflict, "Common-E-004", "[]", "다른 사용자가 먼저 수정했습니다. 다시 조회한 뒤 저장하세요.")]
    public void RefusedSave_ShowsTheApiMessage(HttpStatusCode status, string code, string args, string message)
    {
        _api.Problem = (status, $$"""{"status":{{(int)status}},"code":"{{code}}","args":{{args}}}""");
        var page = RenderAs();

        Grid(page).Edit(1, "widthMm", "0").Should().BeTrue();
        page.Find("[data-testid=label-format-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=label-format-message]").TextContent.Should().Contain(message));
    }

    [Fact, Trait("Id", "MM-UC-036-T03"), Trait("Rule", "MM-BR-918")]
    public void Preview_DownloadsTheSamplePdfOfTheCurrentType()
    {
        var page = RenderAs();
        Grid(page).Select(0);

        page.Find("[data-testid=label-format-toolbar-preview]").Click();

        page.WaitForAssertion(() => _api.Previews.Should().Equal("PALLET"));
        JSInterop.Invocations.Should().Contain(i => i.Identifier == "quroDownload" && Equals(i.Arguments[0], "label-pallet.pdf"));
        page.FindAll("[data-testid=label-format-preview-message]").Should().BeEmpty();
    }

    // security.md "화면 쓰기 권한": the grid and the field dialog follow IScreenAccess for this screen path; the preview only reads
    [Fact, Trait("Id", "BC-UT-214")]
    public void UserWithoutWriteAccess_CanOnlyReadAndPreview()
    {
        var access = new FakeScreenAccess(false);
        Services.AddSingleton<IScreenAccess>(access);

        var page = RenderAs();
        Grid(page).Select(0);

        Grid(page).IsReadOnly(0, "widthMm").Should().BeTrue();
        page.Find("[data-testid=label-format-toolbar-save]").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=label-format-toolbar-fields]").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=label-format-toolbar-preview]").HasAttribute("disabled").Should().BeFalse();
        access.Asked.Should().Equal("/mm/label-format");
    }

    [Fact]
    public void English_ShowsTheEnglishLabels()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("en");

        var page = RenderAs();

        Grid(page).Headers.Select(h => h.Title).Should().Contain(["Label Type", "Width (mm)", "Printed Fields"]);
        Grid(page).Text(0, "fieldNames").Should().Be("Pallet No., Qty");
        page.Find("[data-testid=label-format-toolbar-fields]").TextContent.Should().Be("Choose Fields");
        page.Find("[data-testid=label-format-toolbar-preview]").TextContent.Should().Be("Preview");
    }

    [Fact]
    public void EveryInputButtonAndGrid_HasTestIdByRule()
    {
        var page = RenderAs();
        Grid(page).Select(0);
        page.Find("[data-testid=label-format-toolbar-fields]").Click();

        var elements = page.FindAll("input, select, textarea, button:not([hidden]), .quro-grid");
        elements.Should().NotBeEmpty();
        elements.Select(DataGridPageExtensions.TestIdOf).Should()
            .OnlyContain(id => id != null && System.Text.RegularExpressions.Regex.IsMatch(id, "^label-format(-(search|grid|toolbar|message|fields)(-[a-z0-9]+)*)?$"));
    }

    private static void Configure(HttpClient http) => http.BaseAddress = new Uri("http://api.test");

    private IRenderedComponent<LabelFormat> RenderAs()
    {
        var store = Services.GetRequiredService<AccessTokenStore>();
        var sessionId = store.Add(new LoginResponse("token-admin", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", "admin", false, "ko"));
        _auth.SetAuthorized("kim");
        _auth.SetRoles("admin");
        _auth.SetClaims(new Claim(WebAuthClaims.SessionId, sessionId));
        var page = Render<LabelFormat>();
        page.WaitForAssertion(() => Grid(page).Rows.Should().HaveCount(4));
        return page;
    }

    private static DataGridPage<LabelFormatRow> Grid(IRenderedComponent<LabelFormat> page) =>
        page.DataGrid<LabelFormatRow, LabelFormat>("label-format-grid");

    /// <summary>The four install formats; PALLET prints the Pallet number and qty only.</summary>
    private sealed class FakeApi : HttpMessageHandler
    {
        private static readonly LabelFormatDto[] Formats =
        [
            new("PALLET", "ZPL", 100, 150, 203, ["palletNo", "qty"], ["palletNo", "itemCd", "itemName", "lotNo", "qty", "expiryDt"], true, Read),
            new("ITEM", "ZPL", 50, 25, 203, ["itemCd", "itemName"], ["itemCd", "itemName"], true, Read),
            new("LOT", "ZPL", 50, 25, 203, ["itemCd", "lotNo", "expiryDt"], ["itemCd", "itemName", "lotNo", "expiryDt"], true, Read),
            new("LOCATION", "ZPL", 100, 50, 203, ["warehouseCode", "locationCode"], ["warehouseCode", "locationCode"], true, Read),
        ];

        public int Lists { get; private set; }

        public List<BatchRequest<LabelFormatSaveRow, LabelFormatKey>> Saves { get; } = [];

        public List<string> Previews { get; } = [];

        public (HttpStatusCode Status, string Body)? Problem { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && Problem is { } problem)
            {
                return new HttpResponseMessage(problem.Status) { Content = new StringContent(problem.Body, null, "application/problem+json") };
            }
            if (path == "/" + LabelFormatRoutes.Batch && request.Method == HttpMethod.Post)
            {
                Saves.Add((await request.Content!.ReadFromJsonAsync<BatchRequest<LabelFormatSaveRow, LabelFormatKey>>(cancellationToken))!);
                return Json(Formats);
            }
            if (path == "/" + LabelFormatRoutes.Base)
            {
                Lists++;
                return Json(Formats);
            }
            if (path.EndsWith("/preview.pdf", StringComparison.Ordinal))
            {
                Previews.Add(path.Split('/')[^2]);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("%PDF"u8.ToArray()) };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
