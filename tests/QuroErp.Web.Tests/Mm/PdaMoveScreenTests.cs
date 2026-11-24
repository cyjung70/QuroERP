using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using QuroErp.Contracts.Bc.Items;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Contracts.Mm.Labels;
using QuroErp.Contracts.Mm.Lots;
using QuroErp.Contracts.Mm.StockTransfers;
using QuroErp.Web.Auth;
using QuroErp.Web.Bc.Items;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Layout;
using QuroErp.Web.Components.Pages.Mm;
using QuroErp.Web.Mm.Labels;
using QuroErp.Web.Mm.Lots;
using QuroErp.Web.Mm.StockTransfers;

namespace QuroErp.Web.Tests.Mm;

/// <summary>
/// PDA move /mm/pda-move (MM-UC-039, TASK-MM-062) against a fake API. MAIN (1) has the Locations '*' (40), A-01 (41) and A-02 (42),
/// EAST (2) has '*' (50) and E-01 (51). The lot- and Location-managed resin (31) holds R2611A 20 and R2611B 10 at A-01 and R2611A 5
/// at A-02; the bolt (11) is managed by neither; Pallet PL2611990001 holds 4 of the cable (21, Lot L2611A) at MAIN '*'.
/// </summary>
public class PdaMoveScreenTests : BunitContext
{
    private const string Pallet = "PL|PL2611990001|DEMO-CABLE-LOT|L2611A|4";
    private readonly FakeMoveApi _api = new();
    private readonly BunitAuthorizationContext _auth;

    public PdaMoveScreenTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("ko");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddWebLocalization();
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton<AccessTokenStore>();
        Services.AddScoped<AccessTokenProvider>();
        Services.AddHttpClient<AuthApiClient>(Configure);
        Services.AddHttpClient<LabelScanApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<StockTransferApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<ItemApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<LotApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddScoped<IScreenAccess, ScreenAccess>();
        _auth = AddAuthorization();
    }

    [Fact, Trait("Id", "MM-UC-039-T01"), Trait("Rule", "MM-BR-922"), Trait("Rule", "MM-BR-713"), Trait("Rule", "MM-BR-362")]
    public void TwoCardsInsideOneWarehouse_GoStepByStep_AndSaveAsOneTransferWithoutAType()
    {
        var page = RenderAs("admin");
        StepText(page).Should().Be("보내는 Location 또는 Pallet을 스캔하세요");

        Scan(page, "LC|MAIN|A-01");
        page.WaitForAssertion(() => StepText(page).Should().Be("품목 또는 Lot을 스캔하세요"));
        Scan(page, "DEMO-RESIN-LOC");
        page.WaitForAssertion(() => StepText(page).Should().Be("받는 Location을 스캔하세요"));
        page.Find("[data-testid=pda-move-qty]").Change("2");
        Scan(page, "LC|MAIN|A-02");
        page.WaitForAssertion(() => StepText(page).Should().Be("보내는 Location 또는 Pallet을 스캔하세요"));

        Scan(page, "LC|MAIN|A-01");
        page.WaitForAssertion(() => page.FindAll("[data-testid=pda-move-card]").Should().HaveCount(2));
        Scan(page, "LT|DEMO-RESIN-LOC|R2611B");
        page.WaitForAssertion(() => StepText(page).Should().Be("받는 Location을 스캔하세요"));
        page.FindAll("[data-testid=pda-move-qty]")[1].Change("1");
        Scan(page, "LC|MAIN|A-02");
        page.WaitForAssertion(() => page.Find("[data-testid=pda-move-type]").TextContent.Should().Be("LT"));
        page.Find("[data-testid=pda-move-warehouses]").TextContent.Should().Be("MAIN → MAIN");
        page.FindAll("[data-testid=pda-move-from]").Select(f => f.GetAttribute("value")).Should().Equal("MAIN / A-01", "MAIN / A-01");
        page.FindAll("[data-testid=pda-move-to]").Select(f => f.GetAttribute("value")).Should().Equal("MAIN / A-02", "MAIN / A-02");

        page.Find("[data-testid=pda-move-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=pda-move-notice]").TextContent.Should().Be("이동 ST2611000001(LT)을(를) 저장했습니다."));
        var request = _api.Saved.Should().ContainSingle().Subject;
        (request.MovType, request.TransferDt, request.FromWarehouseId, request.ToWarehouseId)
            .Should().Be(((string?)null, (DateOnly?)DateOnly.FromDateTime(DateTime.Today), (long?)1, (long?)1));
        request.Lines!.Select(l => (l.ItemId, l.Qty, l.LotId, l.FromLocationId, l.ToLocationId, l.PalletId)).Should().Equal(
            ((long?)31, (decimal?)2m, (long?)601, (long?)41, (long?)42, (long?)null),
            ((long?)31, (decimal?)1m, (long?)602, (long?)41, (long?)42, (long?)null));
        page.FindAll("[data-testid=pda-move-card]").Should().BeEmpty("the next transfer starts empty");
        page.Find("[data-testid=pda-move-warehouses]").TextContent.Should().Be("-");
        _api.Queries.Should().Contain("/api/mm/stock-transfers/scan-place?value=LC%7CMAIN%7CA-01&step=FROM")
            .And.Contain("/api/mm/stock-transfers/scan-place?value=LC%7CMAIN%7CA-02&step=TO");
    }

    [Fact, Trait("Id", "MM-UC-039-T02"), Trait("Rule", "MM-BR-922"), Trait("Rule", "MM-BR-713")]
    public void ACardToAnotherWarehouse_IsATr_AndAnItemWithoutLocationsSendsStar()
    {
        var page = RenderAs("admin");

        Scan(page, "LC|MAIN|*");
        Scan(page, "DEMO-BOLT-10");
        page.WaitForAssertion(() => page.FindAll("[data-testid=pda-move-qty]").Should().HaveCount(1));
        page.FindAll("[data-testid=pda-move-lot]").Should().BeEmpty("the bolt is not lot-managed");
        page.Find("[data-testid=pda-move-qty]").Change("5");
        Scan(page, "LC|EAST|*");
        page.WaitForAssertion(() => page.Find("[data-testid=pda-move-type]").TextContent.Should().Be("TR"));

        page.Find("[data-testid=pda-move-save]").Click();

        page.WaitForAssertion(() => _api.Saved.Should().ContainSingle());
        var request = _api.Saved[0];
        (request.MovType, request.FromWarehouseId, request.ToWarehouseId).Should().Be(((string?)null, (long?)1, (long?)2));
        request.Lines!.Select(l => (l.ItemId, l.Qty, l.LotId, l.FromLocationId, l.ToLocationId))
            .Should().Equal(((long?)11, (decimal?)5m, (long?)null, (long?)null, (long?)null));
        page.Find("[data-testid=pda-move-notice]").TextContent.Should().Be("이동 ST2611000001(TR)을(를) 저장했습니다.");
    }

    [Fact, Trait("Id", "MM-UC-039-T03"), Trait("Rule", "MM-BR-912")]
    public void APalletScan_FillsTheCardWithThePallet_WithAFixedQty_AndGoesStraightToTo()
    {
        var page = RenderAs("admin");

        Scan(page, Pallet);

        page.WaitForAssertion(() => page.Find("[data-testid=pda-move-pallet]").GetAttribute("value").Should().Be("PL2611990001"));
        page.Find("[data-testid=pda-move-qty]").HasAttribute("readonly").Should().BeTrue("a Pallet moves whole");
        page.Find("[data-testid=pda-move-qty]").GetAttribute("value").Should().Be("4");
        page.Find("[data-testid=pda-move-lot]").GetAttribute("value").Should().Be("L2611A");
        page.Find(".quro-pda-card-title").TextContent.Should().Be("DEMO-CABLE-LOT");
        StepText(page).Should().Be("받는 Location을 스캔하세요");
        Scan(page, "LC|EAST|*");
        page.WaitForAssertion(() => page.Find("[data-testid=pda-move-to]").GetAttribute("value").Should().Be("EAST / *"));

        page.Find("[data-testid=pda-move-save]").Click();

        page.WaitForAssertion(() => _api.Saved.Should().ContainSingle());
        _api.Saved[0].Lines!.Select(l => (l.ItemId, l.Qty, l.LotId, l.FromLocationId, l.ToLocationId, l.PalletId))
            .Should().Equal(((long?)21, (decimal?)4m, (long?)501, (long?)null, (long?)null, (long?)900));
    }

    [Fact, Trait("Id", "MM-UC-039-T04"), Trait("Rule", "MM-BR-715"), Trait("Rule", "MM-BR-923"), Trait("Rule", "MM-BR-713")]
    public void TheSameLocation_UnknownPlaces_AndItemsKeptOnStar_AreRefused()
    {
        var page = RenderAs("admin");
        Scan(page, "LC|MAIN|Z9");
        page.WaitForAssertion(() => Error(page).Should().Be("읽을 수 없는 바코드입니다: LC|MAIN|Z9"));
        Scan(page, "DEMO-BOLT-10");
        page.WaitForAssertion(() => Error(page).Should().Be("읽을 수 없는 바코드입니다: DEMO-BOLT-10"));
        page.FindAll("[data-testid=pda-move-card]").Should().BeEmpty();

        Scan(page, "LC|MAIN|A-01");
        page.WaitForAssertion(() => page.FindAll("[data-testid=pda-move-card]").Should().HaveCount(1));
        Scan(page, "DEMO-BOLT-10");
        page.WaitForAssertion(() => Error(page).Should().Be("DEMO-BOLT-10은(는) Location 관리 품목이 아니어서 Location '*'만 씁니다."));
        Scan(page, "LT|DEMO-RESIN-LOC|R9");
        page.WaitForAssertion(() => Error(page).Should().Be("A-01에 Lot R9의 재고가 없습니다."));
        Scan(page, "DEMO-RESIN-LOC");
        page.WaitForAssertion(() => StepText(page).Should().Be("받는 Location을 스캔하세요"));
        Scan(page, Pallet);
        page.WaitForAssertion(() => Error(page).Should().Be("읽을 수 없는 바코드입니다: " + Pallet));
        Scan(page, "LC|MAIN|A-01");
        page.WaitForAssertion(() => Error(page).Should().Be("MAIN / A-01은(는) 보내는 곳과 같은 Location입니다."));

        page.Find("[data-testid=pda-move-to]").GetAttribute("value").Should().Be("-", "a refused scan changes nothing");
        JSInterop.Invocations["quroPda.reject"].Should().HaveCount(6);
    }

    [Fact, Trait("Id", "MM-UC-039-T01"), Trait("Rule", "MM-BR-922")]
    public void TheFirstCard_FixesBothWarehouses_AndACardOfAnotherIsRefusedUntilTheSave()
    {
        var page = RenderAs("admin");
        Scan(page, "LC|MAIN|*");
        Scan(page, "DEMO-BOLT-10");
        page.WaitForAssertion(() => page.FindAll("[data-testid=pda-move-qty]").Should().HaveCount(1));
        Scan(page, "LC|EAST|*");
        page.WaitForAssertion(() => page.Find("[data-testid=pda-move-warehouses]").TextContent.Should().Be("MAIN → EAST"));

        Scan(page, "LC|EAST|E-01");
        page.WaitForAssertion(() => Error(page).Should().Be("EAST은(는) 이 이동의 보내는 창고 MAIN이(가) 아닙니다. 저장한 뒤 새로 시작하세요."));
        Scan(page, "LC|MAIN|A-01");
        Scan(page, "DEMO-RESIN-LOC");
        page.WaitForAssertion(() => StepText(page).Should().Be("받는 Location을 스캔하세요"));
        Scan(page, "LC|MAIN|A-02");
        page.WaitForAssertion(() => Error(page).Should().Be("MAIN은(는) 이 이동의 받는 창고 EAST이(가) 아닙니다. 저장한 뒤 새로 시작하세요."));
        Scan(page, "LC|EAST|E-01");
        page.WaitForAssertion(() => page.FindAll("[data-testid=pda-move-to]")[1].GetAttribute("value").Should().Be("EAST / E-01"));

        page.FindAll("[data-testid=pda-move-remove]")[0].Click();
        page.FindAll("[data-testid=pda-move-remove]")[0].Click();
        page.Find("[data-testid=pda-move-warehouses]").TextContent.Should().Be("-", "without cards the warehouses are free again");
    }

    [Fact, Trait("Id", "MM-UC-039-T06"), Trait("Rule", "MM-BR-362"), Trait("Rule", "MM-BR-355")]
    public void TheLotList_ShowsTheLotsAtTheSendingLocation_AndPicksTheLine()
    {
        var page = RenderAs("admin");
        Scan(page, "LC|MAIN|A-01");
        Scan(page, "DEMO-RESIN-LOC");
        page.WaitForAssertion(() => page.FindAll("[data-testid=pda-move-lot]").Should().HaveCount(1));

        var lot = page.Find("[data-testid=pda-move-lot]");
        lot.QuerySelectorAll("option").Select(o => o.TextContent.Trim()).Should().Equal("*", "R2611A · 20", "R2611B · 10");
        lot.GetAttribute("value").Should().Be("601", "an item scan picks the first Lot at the Location");
        lot.Change("602");
        page.Find("[data-testid=pda-move-qty]").Change("3");
        Scan(page, "LC|EAST|E-01");
        page.WaitForAssertion(() => page.Find("[data-testid=pda-move-type]").TextContent.Should().Be("TR"));
        page.Find("[data-testid=pda-move-save]").Click();

        page.WaitForAssertion(() => _api.Saved.Should().ContainSingle());
        _api.Saved[0].Lines!.Select(l => (l.LotId, l.FromLocationId, l.ToLocationId))
            .Should().Equal(((long?)602, (long?)41, (long?)51));
        _api.Queries.Should().Contain($"/api/mm/lots?itemId=31&warehouseId=1&date={Today}");
    }

    [Fact, Trait("Id", "MM-UC-039-T07"), Trait("Rule", "MM-BR-001")]
    public void AnUnfinishedCard_IsNotSaved_AndServerStockErrorsAreShownWithTheCardsKept()
    {
        var page = RenderAs("admin");
        Scan(page, "LC|MAIN|*");
        Scan(page, "DEMO-BOLT-10");
        page.WaitForAssertion(() => page.FindAll("[data-testid=pda-move-qty]").Should().HaveCount(1));
        page.Find("[data-testid=pda-move-qty]").Change("999");

        page.Find("[data-testid=pda-move-save]").Click();
        page.Find("[data-testid=pda-move-error]").TextContent.Should().Be("받는 Location을 아직 스캔하지 않은 카드가 있습니다.");
        _api.Saved.Should().BeEmpty();

        _api.SaveProblem = """{"status":422,"code":"MM-E-001","args":["DEMO-BOLT-10","MAIN","10","999"]}""";
        Scan(page, "LC|EAST|*");
        page.WaitForAssertion(() => page.Find("[data-testid=pda-move-type]").TextContent.Should().Be("TR"));
        page.Find("[data-testid=pda-move-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=pda-move-error]").TextContent.Should().Contain("DEMO-BOLT-10"));
        page.FindAll("[data-testid=pda-move-card]").Should().HaveCount(1);
    }

    [Fact, Trait("Id", "MM-UC-039-T05"), Trait("Rule", "BC-BR-214")]
    public void WithoutWriteAccessOnTheTransferScreen_SaveStaysOff_TheCardFieldsUseTheEntryColors_AndDisabledButtonsAreShown()
    {
        var page = RenderAs("user");
        page.Find("[data-testid=pda-move-clear]").HasAttribute("disabled").Should().BeTrue("shown, not hidden, without cards");
        Scan(page, "LC|MAIN|A-01");
        Scan(page, "DEMO-RESIN-LOC");
        page.WaitForAssertion(() => page.FindAll("[data-testid=pda-move-qty]").Should().HaveCount(1));
        Scan(page, "LC|MAIN|A-02");
        page.WaitForAssertion(() => page.Find("[data-testid=pda-move-type]").TextContent.Should().Be("LT"));

        page.Find("[data-testid=pda-move-save]").HasAttribute("disabled").Should().BeTrue();
        page.FindAll(".quro-pda-actions > button").Select(b => b.TextContent.Trim()).Should().Equal("모두 지우기", "저장");
        Field(page, "qty").ClassList.Should().Contain(["quro-field-input", "quro-field-required"]);
        Field(page, "qty").QuerySelector(".quro-required")!.TextContent.Should().Be("*");
        Field(page, "from").ClassList.Should().Contain("quro-field-readonly");
        Field(page, "to").ClassList.Should().Contain("quro-field-readonly");
        Field(page, "lot").ClassList.Should().Contain("quro-field-input").And.NotContain("quro-field-required");
        ShellRoutes.Resolve("/mm/pda-move").Should().BeNull("a PDA page is not a screen tab");
    }

    [Fact, Trait("Id", "MM-UC-039-T05")]
    public void InEnglish_TheTitleStepsAndFieldsComeFromTheResources()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("en");
        var page = RenderAs("admin");
        page.Find("h1").TextContent.Should().Be("PDA Move");
        StepText(page).Should().Be("Scan the sending Location or Pallet");

        Scan(page, Pallet);

        page.WaitForAssertion(() => StepText(page).Should().Be("Scan the receiving Location"));
        Field(page, "qty").QuerySelector("label")!.TextContent.Should().Be("Move qty");
        Field(page, "from").QuerySelector("label")!.TextContent.Should().Be("From");
    }

    private static string Today => DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private IRenderedComponent<PdaMove> RenderAs(string role)
    {
        var store = Services.GetRequiredService<AccessTokenStore>();
        var sessionId = store.Add(new LoginResponse($"token-{role}", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", role, false, "ko"));
        _auth.SetAuthorized("kim");
        _auth.SetRoles(role);
        _auth.SetClaims(new Claim(WebAuthClaims.SessionId, sessionId));
        return Render<PdaMove>();
    }

    private static void Scan(IRenderedComponent<PdaMove> page, string value)
    {
        page.Find("[data-testid=pda-move-scan-input]").Input(value);
        page.Find("[data-testid=pda-move-scan-input]").KeyDown(new KeyboardEventArgs { Key = "Enter" });
    }

    private static string StepText(IRenderedComponent<PdaMove> page) => page.Find(".quro-pda-scan-label").TextContent;

    private static string Error(IRenderedComponent<PdaMove> page) => page.Find("[data-testid=pda-move-scan-error]").TextContent;

    private static AngleSharp.Dom.IElement Field(IRenderedComponent<PdaMove> page, string name) =>
        page.Find($"[data-testid=pda-move-{name}]").ParentElement!;

    private static void Configure(HttpClient http) => http.BaseAddress = new Uri("http://api.test");

    private sealed class FakeMoveApi : HttpMessageHandler
    {
        private static readonly StockTransferPalletDto CablePallet = new(900, "PL2611990001", 21, "DEMO-CABLE-LOT", "Demo Cable Reel (lot)", 501, "L2611A", 4m);

        private static readonly Dictionary<string, StockTransferPlaceDto> Locations = new()
        {
            ["LC|MAIN|*"] = new("LC", 1, "MAIN", 40, "*", null),
            ["LC|MAIN|A-01"] = new("LC", 1, "MAIN", 41, "A-01", null),
            ["LC|MAIN|A-02"] = new("LC", 1, "MAIN", 42, "A-02", null),
            ["LC|EAST|*"] = new("LC", 2, "EAST", 50, "*", null),
            ["LC|EAST|E-01"] = new("LC", 2, "EAST", 51, "E-01", null),
        };

        private static readonly LotBalanceDto[] ResinBalances =
        [
            new(601, "R2611A", 20m, 41, "A-01"), new(602, "R2611B", 10m, 41, "A-01"), new(601, "R2611A", 5m, 42, "A-02"),
        ];

        public List<string> Queries { get; } = [];

        public List<SaveStockTransferRequest> Saved { get; } = [];

        public string? SaveProblem { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var path = uri.AbsolutePath;
            var pairs = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2))
                .Select(p => (Name: p[0], Value: Uri.UnescapeDataString(p[1]))).ToList();
            var query = pairs.GroupBy(p => p.Name).ToDictionary(g => g.Key, g => g.First().Value);
            if (path == "/" + LabelScanRoutes.Base)
            {
                var accept = pairs.Where(p => p.Name == "accept").Select(p => p.Value).ToList();
                var scan = ReadLabel(query["value"]);
                return scan is null || (accept.Count > 0 && !accept.Contains(scan.Kind)) ? Problem("MM-E-923", query["value"]) : Ok(scan);
            }
            Queries.Add(uri.PathAndQuery);
            if (path == "/" + StockTransferRoutes.ScanPlace)
            {
                var value = query["value"];
                if (Locations.TryGetValue(value, out var location))
                {
                    return Ok(location);
                }
                return value == Pallet && query["step"] == "FROM"
                    ? Ok(new StockTransferPlaceDto("PL", 1, "MAIN", 40, "*", CablePallet))
                    : Problem("MM-E-923", value);
            }
            if (path.StartsWith("/" + ItemRoutes.Base + "/", StringComparison.Ordinal))
            {
                return path[(ItemRoutes.Base.Length + 2)..] switch
                {
                    "31" => Json("""{"id":31,"itemCd":"DEMO-RESIN-LOC","itemName":"Demo Resin (Lot, Location)","lotManaged":true,"locationManaged":true}"""),
                    "21" => Json("""{"id":21,"itemCd":"DEMO-CABLE-LOT","itemName":"Demo Cable Reel (lot)","lotManaged":true,"locationManaged":false}"""),
                    "11" => Json("""{"id":11,"itemCd":"DEMO-BOLT-10","itemName":"Demo Hex Bolt M10","lotManaged":false,"locationManaged":false}"""),
                    _ => new HttpResponseMessage(HttpStatusCode.NotFound),
                };
            }
            if (path == "/" + LotRoutes.Base)
            {
                return Ok<LotBalanceDto[]>(query["itemId"] == "31" ? ResinBalances : []);
            }
            if (request.Method == HttpMethod.Post && path == "/" + StockTransferRoutes.Base)
            {
                var save = (await request.Content!.ReadFromJsonAsync<SaveStockTransferRequest>(cancellationToken))!;
                Saved.Add(save);
                if (SaveProblem is not null)
                {
                    return new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
                    {
                        Content = new StringContent(SaveProblem, Encoding.UTF8, "application/problem+json"),
                    };
                }
                var type = save.ToWarehouseId == save.FromWarehouseId ? "LT" : "TR";
                return Json(
                    $$"""{"header":{"id":7,"transferNo":"ST2611000001","movType":"{{type}}","lineCount":{{save.Lines!.Count}}},"lines":[]}""",
                    HttpStatusCode.Created);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        // Like the scan API: item codes come back as IT, LT and PL labels by their kind; a kind outside accept is MM-E-923
        private static LabelScanDto? ReadLabel(string value)
        {
            var parts = value.Split('|');
            return parts[0] switch
            {
                "DEMO-RESIN-LOC" => new LabelScanDto("IT", [value], 31, value, "Demo Resin (Lot, Location)"),
                "DEMO-BOLT-10" => new LabelScanDto("IT", [value], 11, value, "Demo Hex Bolt M10"),
                "LT" => new LabelScanDto("LT", parts[1..], 31, "DEMO-RESIN-LOC", null),
                "PL" => new LabelScanDto("PL", parts[1..], 21, "DEMO-CABLE-LOT", null),
                "LC" => new LabelScanDto("LC", parts[1..], null, null, null),
                _ => null,
            };
        }

        private static HttpResponseMessage Ok<T>(T body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };

        private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        private static HttpResponseMessage Problem(string code, string arg) => new(HttpStatusCode.UnprocessableEntity)
        {
            Content = JsonContent.Create(new { status = 422, code, args = new[] { arg } }),
        };
    }
}
