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
using QuroErp.Contracts.Bc.UserProfiles;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Contracts.Mm.Labels;
using QuroErp.Contracts.Mm.Locations;
using QuroErp.Contracts.Mm.Lots;
using QuroErp.Contracts.Mm.Warehouses;
using QuroErp.Contracts.Sd.Shipments;
using QuroErp.Web.Auth;
using QuroErp.Web.Bc.UserProfiles;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Layout;
using QuroErp.Web.Components.Pages.Sd;
using QuroErp.Web.Components.Screens;
using QuroErp.Web.Mm.Labels;
using QuroErp.Web.Mm.Locations;
using QuroErp.Web.Mm.Lots;
using QuroErp.Web.Mm.Warehouses;
using QuroErp.Web.Sd.Shipments;
using QuroErp.Web.Tests.Screens;

namespace QuroErp.Web.Tests.Sd;

/// <summary>
/// PDA shipment /sd/pda-issue (SD-UC-054, TASK-SD-071) against a fake API. Order DEMO-SO-PDA of BLUE RIVER MARKET has a lot- and
/// Location-managed cable line (item 21, 25 left) and a bolt line (item 11, 20 left). The default warehouse MAIN (1) holds the
/// cable in Lot L2611A 22 and L2611B 17 at '*' and L2611B 5 at A1; Pallet PL2611990001 holds 4 of L2611A. BILL_THEN_SHIP invoice
/// DN2611000005 has a bolt line left to ship from the consignment warehouse CONS (3). SO-CLOSED is a closed order.
/// </summary>
public class PdaIssueScreenTests : BunitContext
{
    private const string So = "DEMO-SO-PDA";
    private const string Dn = "DN2611000005";
    private readonly FakePdaApi _api = new();
    private readonly FakeConfirm _confirm = new();
    private readonly BunitAuthorizationContext _auth;

    public PdaIssueScreenTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("ko");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddWebLocalization();
        Services.AddSingleton<IQuroConfirm>(_confirm);
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton<AccessTokenStore>();
        Services.AddScoped<AccessTokenProvider>();
        Services.AddHttpClient<AuthApiClient>(Configure);
        Services.AddHttpClient<LabelScanApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<ShipmentApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<LotApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<LocationApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<WarehouseApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<UserProfileApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddScoped<IScreenAccess, ScreenAccess>();
        _auth = AddAuthorization();
    }

    [Fact, Trait("Id", "SD-UC-054-T01"), Trait("Rule", "MM-BR-921"), Trait("Rule", "MM-BR-716")]
    public void ScanningTheOrder_ShowsTheCustomerAndLines_AndAnItemBecomesFifoCards()
    {
        var page = RenderAs("admin");
        page.Find(".quro-pda-scan-label").TextContent.Should().Be("수주 또는 송장 번호를 스캔하세요");

        Scan(page, "demo-so-pda");

        page.WaitForAssertion(() => page.Find("[data-testid=pda-issue-no]").TextContent.Should().Be(So));
        page.Find("[data-testid=pda-issue-customer]").TextContent.Should().Be("BLUE RIVER MARKET");
        page.Find("[data-testid=pda-issue-warehouse]").TextContent.Should().Be("MAIN");
        page.FindAll("[data-testid=pda-issue-open-line]").Select(l => l.TextContent)
            .Should().Equal("1 · DEMO-CABLE-LOT · 25 EA", "2 · DEMO-BOLT-10 · 20 EA");
        page.Find(".quro-pda-scan-label").TextContent.Should().Be("품목을 스캔하세요");

        Scan(page, "DEMO-CABLE-LOT");

        page.WaitForAssertion(() => page.FindAll("[data-testid=pda-issue-card]").Should().HaveCount(2));
        page.FindAll("[data-testid=pda-issue-location]").Select(l => l.GetAttribute("value")).Should().Equal("* · L2611A", "* · L2611B");
        page.FindAll("[data-testid=pda-issue-qty]").Select(q => q.GetAttribute("value")).Should().Equal("22", "3");
        page.Find(".quro-pda-scan-label").TextContent.Should().Be("품목·Pallet·Location·Lot을 스캔하세요");
        _api.Queries.Should().Contain("/api/mm/stock-suggestions?itemId=21&warehouseId=1&qty=25&date=" + Today);
    }

    [Fact, Trait("Id", "SD-UC-054-T02"), Trait("Rule", "MM-BR-921"), Trait("Rule", "MM-BR-362"), Trait("Rule", "MM-BR-713")]
    public void ALotScan_ALocationScan_AndTheLotList_ChangeTheSelectedCard()
    {
        var page = OrderWithCable();

        Scan(page, "LT|DEMO-CABLE-LOT|L2611B");
        page.WaitForAssertion(() => Location(page, 0).Should().Be("* · L2611B"));

        Scan(page, "LC|MAIN|A1");
        page.WaitForAssertion(() => Location(page, 0).Should().Be("A1 · L2611B"));

        page.FindAll("[data-testid=pda-issue-lot]")[0].Change("30-501");
        page.WaitForAssertion(() => Location(page, 0).Should().Be("* · L2611A"));
        page.FindAll("[data-testid=pda-issue-lot]")[0].QuerySelectorAll("option").Select(o => o.TextContent.Trim())
            .Should().Equal("-", "L2611A · * · 22", "L2611B · * · 17", "L2611B · A1 · 5");
    }

    [Fact, Trait("Id", "SD-UC-054-T03"), Trait("Rule", "MM-BR-913"), Trait("Rule", "MM-BR-713"), Trait("Rule", "MM-BR-363")]
    public void APalletScan_ShipsThePalletWhole_AndSaveSendsTheLinesToTheShipmentApi()
    {
        var page = OrderWithCable();
        page.FindAll("[data-testid=pda-issue-remove]")[1].Click();

        Scan(page, "PL|PL2611990001|DEMO-CABLE-LOT|L2611A|4");
        page.WaitForAssertion(() => page.Find("[data-testid=pda-issue-pallet]").GetAttribute("value").Should().Be("PL2611990001"));
        page.Find("[data-testid=pda-issue-qty]").HasAttribute("readonly").Should().BeTrue("a Pallet ships whole");
        page.Find("[data-testid=pda-issue-qty]").GetAttribute("value").Should().Be("4");
        Scan(page, "DEMO-BOLT-10");
        page.WaitForAssertion(() => page.FindAll("[data-testid=pda-issue-card]").Should().HaveCount(2));
        page.FindAll("[data-testid=pda-issue-qty]")[1].Change("12");

        page.Find("[data-testid=pda-issue-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=pda-issue-notice]").TextContent.Should().Be("출하 SH2611000003을(를) 저장했습니다."));
        var request = _api.Saved.Should().ContainSingle().Subject;
        (request.CustomerId, request.WarehouseId, request.CurrencyCode, request.ShipName, request.ConfirmCreditLimit)
            .Should().Be((40L, (long?)1, "USD", "Blue River Dock", (bool?)null));
        request.Lines.Select(l => (l.ItemId, l.WarehouseId, l.ShipQty, l.LotId, l.SalesOrderLineId, l.SalesInvoiceLineId, l.LocationId, l.PalletId))
            .Should().Equal(
                (21L, (long?)1, 4m, (long?)501, (long?)201, (long?)null, (long?)null, (long?)900),
                (11L, (long?)1, 12m, (long?)null, (long?)202, (long?)null, (long?)null, (long?)null));
        page.FindAll("[data-testid=pda-issue-card]").Should().BeEmpty("the next order starts empty");
        page.Find("[data-testid=pda-issue-no]").TextContent.Should().Be("-");
    }

    [Fact, Trait("Id", "SD-UC-054-T04"), Trait("Rule", "MM-BR-923")]
    public void UnknownOrClosedNumbers_AnotherItemsLabel_AndAStockKeyWithoutStock_AreRejected()
    {
        var page = RenderAs("admin");
        Scan(page, "NOPE");
        page.WaitForAssertion(() => Error(page).Should().Be("읽을 수 없는 바코드입니다: NOPE"));
        Scan(page, "SO-CLOSED");
        page.WaitForAssertion(() => Error(page).Should().Be("수주 SO-CLOSED은(는) 마감되어 출하할 줄이 없습니다."));
        page.Find("[data-testid=pda-issue-no]").TextContent.Should().Be("-");

        Scan(page, So);
        page.WaitForAssertion(() => page.Find("[data-testid=pda-issue-no]").TextContent.Should().Be(So));
        Scan(page, "DEMO-FREIGHT");
        page.WaitForAssertion(() => Error(page).Should().Be("읽을 수 없는 바코드입니다: DEMO-FREIGHT"));
        Scan(page, "DEMO-CABLE-LOT");
        page.WaitForAssertion(() => page.FindAll("[data-testid=pda-issue-card]").Should().HaveCount(2));

        Scan(page, "LT|DEMO-BOLT-10|L1");
        page.WaitForAssertion(() => Error(page).Should().Be("DEMO-BOLT-10은(는) 이 카드의 품목이 아닙니다."));
        Scan(page, "PL|PL2611990009|DEMO-BOLT-10|*|50");
        page.WaitForAssertion(() => Error(page).Should().Be("DEMO-BOLT-10은(는) 이 카드의 품목이 아닙니다."));
        Scan(page, "PL|PL2611999999|DEMO-CABLE-LOT|L2611A|4");
        page.WaitForAssertion(() => Error(page).Should().Be("Pallet PL2611999999은(는) 출고 창고에 있는 ACTIVE Pallet이 아닙니다."));
        Scan(page, "LC|SECOND|A1");
        page.WaitForAssertion(() => Error(page).Should().Be("Location SECOND / A1은(는) 출고 창고 MAIN에 없습니다."));
        Scan(page, "LC|MAIN|B9");
        page.WaitForAssertion(() => Error(page).Should().Be("B9에 이 품목의 재고가 없습니다."));
        Scan(page, "LT|DEMO-CABLE-LOT|L9");
        page.WaitForAssertion(() => Error(page).Should().Be("L9에 이 품목의 재고가 없습니다."));

        Location(page, 0).Should().Be("* · L2611A", "a refused scan changes nothing");
        JSInterop.Invocations["quroPda.reject"].Should().HaveCount(9);
    }

    [Fact, Trait("Id", "SD-UC-054-T01"), Trait("Rule", "MM-BR-921"), Trait("Rule", "SD-BR-176")]
    public void ABillThenShipInvoiceNumber_ShipsItsLinesFromTheConsignmentWarehouse()
    {
        var page = RenderAs("admin");
        Scan(page, Dn);
        page.WaitForAssertion(() => page.Find("[data-testid=pda-issue-no]").TextContent.Should().Be(Dn));
        page.Find("[data-testid=pda-issue-warehouse]").TextContent.Should().Be("CONS");
        Scan(page, "DEMO-BOLT-10");
        page.WaitForAssertion(() => page.FindAll("[data-testid=pda-issue-card]").Should().HaveCount(1));

        page.Find("[data-testid=pda-issue-save]").Click();

        page.WaitForAssertion(() => _api.Saved.Should().ContainSingle());
        var request = _api.Saved[0];
        (request.WarehouseId, request.CurrencyCode).Should().Be(((long?)3, "USD"));
        request.Lines.Select(l => (l.ItemId, l.ShipQty, l.SalesOrderLineId, l.SalesInvoiceLineId))
            .Should().Equal((11L, 6m, (long?)null, (long?)301));
    }

    [Fact, Trait("Id", "SD-UC-054-T07"), Trait("Rule", "SD-BR-309")]
    public void CreditLimit_AsksLikeThePcScreen_NoKeepsTheCards_AndServerStockErrorsAreShown()
    {
        _api.SaveProblem = """{"status":422,"code":"SD-W-309","args":["5000","5518"],"confirmField":"confirmCreditLimit"}""";
        _confirm.Answer = false;
        var page = OrderWithCable();

        page.Find("[data-testid=pda-issue-save]").Click();

        page.WaitForAssertion(() => _confirm.Questions.Should().ContainSingle());
        page.Find("[data-testid=pda-issue-error]").TextContent.Should().Be("저장하지 않았습니다. 신용 한도를 확인하세요.");
        page.FindAll("[data-testid=pda-issue-card]").Should().HaveCount(2);

        _api.SaveProblem = """{"status":422,"code":"MM-E-001","args":["DEMO-CABLE-LOT","MAIN","20","25"]}""";
        page.Find("[data-testid=pda-issue-save]").Click();
        page.WaitForAssertion(() => page.Find("[data-testid=pda-issue-error]").TextContent.Should().Contain("DEMO-CABLE-LOT"));
        page.FindAll("[data-testid=pda-issue-card]").Should().HaveCount(2);
    }

    [Fact, Trait("Id", "SD-UC-054-T05"), Trait("Rule", "BC-BR-214")]
    public void WithoutWriteAccessOnTheShipmentScreen_SaveStaysOff_AndDisabledButtonsAreShown()
    {
        var page = RenderAs("user");
        page.Find("[data-testid=pda-issue-change-no]").HasAttribute("disabled").Should().BeTrue("shown, not hidden, before a number");
        Scan(page, So);
        page.WaitForAssertion(() => page.Find("[data-testid=pda-issue-no]").TextContent.Should().Be(So));
        Scan(page, "DEMO-BOLT-10");
        page.WaitForAssertion(() => page.FindAll("[data-testid=pda-issue-card]").Should().HaveCount(1));

        page.Find("[data-testid=pda-issue-save]").HasAttribute("disabled").Should().BeTrue();
        page.FindAll(".quro-pda-actions > button").Select(b => b.TextContent.Trim()).Should().Equal("번호 바꾸기", "저장");
    }

    [Fact, Trait("Id", "SD-UC-054-T05")]
    public void TheCardFields_UseTheEntryColors_AndThePageIsAPdaPage()
    {
        var page = OrderWithCable();

        Field(page, "qty").ClassList.Should().Contain(["quro-field-input", "quro-field-required"]);
        Field(page, "qty").QuerySelector(".quro-required")!.TextContent.Should().Be("*");
        Field(page, "location").ClassList.Should().Contain("quro-field-readonly");
        Field(page, "lot").ClassList.Should().Contain("quro-field-input").And.NotContain("quro-field-required");
        ShellRoutes.Resolve("/sd/pda-issue").Should().BeNull("a PDA page is not a screen tab");
    }

    private static string Today => DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private IRenderedComponent<PdaIssue> OrderWithCable()
    {
        var page = RenderAs("admin");
        Scan(page, So);
        page.WaitForAssertion(() => page.Find("[data-testid=pda-issue-no]").TextContent.Should().Be(So));
        Scan(page, "DEMO-CABLE-LOT");
        page.WaitForAssertion(() => page.FindAll("[data-testid=pda-issue-card]").Should().HaveCount(2));
        return page;
    }

    private IRenderedComponent<PdaIssue> RenderAs(string role)
    {
        var store = Services.GetRequiredService<AccessTokenStore>();
        var sessionId = store.Add(new LoginResponse($"token-{role}", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", role, false, "ko"));
        _auth.SetAuthorized("kim");
        _auth.SetRoles(role);
        _auth.SetClaims(new Claim(WebAuthClaims.SessionId, sessionId));
        return Render<PdaIssue>();
    }

    private static void Scan(IRenderedComponent<PdaIssue> page, string value)
    {
        page.Find("[data-testid=pda-issue-scan-input]").Input(value);
        page.Find("[data-testid=pda-issue-scan-input]").KeyDown(new KeyboardEventArgs { Key = "Enter" });
    }

    private static string Error(IRenderedComponent<PdaIssue> page) => page.Find("[data-testid=pda-issue-scan-error]").TextContent;

    private static string? Location(IRenderedComponent<PdaIssue> page, int card) =>
        page.FindAll("[data-testid=pda-issue-location]")[card].GetAttribute("value");

    private static AngleSharp.Dom.IElement Field(IRenderedComponent<PdaIssue> page, string name) =>
        page.Find($"[data-testid=pda-issue-{name}]").ParentElement!;

    private static void Configure(HttpClient http) => http.BaseAddress = new Uri("http://api.test");

    private sealed class FakePdaApi : HttpMessageHandler
    {
        private static readonly OpenSalesOrderLineDto Cable = new(
            201, 1, 21, "DEMO-CABLE-LOT", "Demo Cable Reel (lot)", "EA", 25m, 0m, 25m, 20m, null, 0m, 4, true, true);

        private static readonly OpenSalesOrderLineDto Bolt = new(
            202, 2, 11, "DEMO-BOLT-10", "Demo Hex Bolt M10", "EA", 20m, 0m, 20m, 0.9m, null, 0m, 4, false, false);

        private static readonly UnshippedInvoiceLineDto InvoiceBolt = new(
            301, 70, Dn, 1, 11, "DEMO-BOLT-10", "Demo Hex Bolt M10", 6m, 0m, 0m, 6m, 0.9m, null, 0m, 4, "USD", 1m, "EA", false, false, 40,
            "BLUE RIVER MARKET");

        private static readonly LotBalanceDto[] CableBalances =
        [
            new(501, "L2611A", 22m, 30, "*"), new(502, "L2611B", 17m, 30, "*"), new(502, "L2611B", 5m, 31, "A1"),
        ];

        public List<string> Queries { get; } = [];

        public List<SaveShipmentRequest> Saved { get; } = [];

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
            if (path.EndsWith("/" + ShipmentRoutes.OpenOrderLines, StringComparison.Ordinal))
            {
                return OrderLines(query);
            }
            if (path.EndsWith("/" + ShipmentRoutes.UnshippedInvoiceLines, StringComparison.Ordinal))
            {
                if (!string.Equals(query["dnNo"], Dn, StringComparison.OrdinalIgnoreCase))
                {
                    return Problem("MM-E-923", query["dnNo"]);
                }
                return query.TryGetValue("itemId", out var itemId) && itemId != "11" ? Problem("MM-E-923", itemId) : Ok(new[] { InvoiceBolt });
            }
            if (path.EndsWith("/" + ShipmentRoutes.Pallets, StringComparison.Ordinal))
            {
                return Ok<ShipmentPalletDto[]>(query["palletNo"] switch
                {
                    "PL2611990001" => new[] { new ShipmentPalletDto(900, "PL2611990001", 21, "DEMO-CABLE-LOT", null, "EA", 1, "MAIN", 30, "*", 501, "L2611A", 4m) },
                    "PL2611990009" => [new ShipmentPalletDto(909, "PL2611990009", 11, "DEMO-BOLT-10", null, "EA", 1, "MAIN", 30, "*", 500, "*", 50m)],
                    _ => [],
                });
            }
            if (path == "/" + LotRoutes.Suggestions)
            {
                var qty = decimal.Parse(query["qty"], CultureInfo.InvariantCulture);
                return Ok(query["itemId"] == "21"
                    ? new StockSuggestionResultDto([new(30, "*", 501, "L2611A", 22m), new(30, "*", 502, "L2611B", 3m)], 0m)
                    : new StockSuggestionResultDto([new(30, "*", 500, "*", qty)], 0m));
            }
            if (path == "/" + LotRoutes.Base)
            {
                return Ok<LotBalanceDto[]>(query["itemId"] == "21" ? CableBalances : []);
            }
            if (path == "/" + WarehouseRoutes.Options)
            {
                return Ok(new[]
                {
                    new WarehouseOptionDto(1, "MAIN", "Main", "NORMAL", true), new WarehouseOptionDto(2, "SECOND", "Second", "NORMAL", true),
                    new WarehouseOptionDto(3, "CONS", "Consignment", "CONSIGNMENT", true),
                });
            }
            if (path == "/" + WarehouseRoutes.Base)
            {
                return Json("""[{"id":1,"warehouseCode":"MAIN","warehouseName":"Main","isDefault":true,"useYn":true,"warehouseType":"NORMAL"}]""");
            }
            if (path == "/" + UserProfileRoutes.Base)
            {
                return Json("""{"userId":"kim"}""");
            }
            if (path == "/" + LocationRoutes.Options)
            {
                return Ok(new[] { new LocationOptionDto(30, "*", null), new LocationOptionDto(31, "A1", "Shelf A1"), new LocationOptionDto(32, "B9", null) });
            }
            if (request.Method == HttpMethod.Post && path == "/" + ShipmentRoutes.Base)
            {
                var save = (await request.Content!.ReadFromJsonAsync<SaveShipmentRequest>(cancellationToken))!;
                Saved.Add(save);
                if (SaveProblem is not null && save.ConfirmCreditLimit is null)
                {
                    return new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
                    {
                        Content = new StringContent(SaveProblem, Encoding.UTF8, "application/problem+json"),
                    };
                }
                return Json("""{"id":88,"shipNo":"SH2611000003","status":"BL"}""", HttpStatusCode.Created);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage OrderLines(Dictionary<string, string> query)
        {
            var soNo = query["soNo"].ToUpperInvariant();
            if (soNo == "SO-CLOSED")
            {
                return Problem("SD-E-172", soNo);
            }
            if (soNo != So)
            {
                return Problem("MM-E-923", query["soNo"]);
            }
            OpenSalesOrderLineDto[] lines = [Cable, Bolt];
            if (query.TryGetValue("itemId", out var itemId))
            {
                lines = [.. lines.Where(l => l.ItemId.ToString(CultureInfo.InvariantCulture) == itemId)];
                if (lines.Length == 0)
                {
                    return Problem("MM-E-923", "DEMO-FREIGHT");
                }
            }
            return Ok(new[]
            {
                new OpenSalesOrderDto(
                    10, So, new DateOnly(2026, 11, 2), "USD", 1m, "SHIP_AND_BILL", "DEMO_A", null, "Blue River Dock", "10 Sample Way", null,
                    "Springfield", "CA", "90010", "USA", null, lines, 40, "BLUE RIVER MARKET"),
            });
        }

        // Like the scan API: item codes come back as IT; LC, LT and PL labels by their kind; a kind outside accept is MM-E-923
        private static LabelScanDto? ReadLabel(string value)
        {
            var parts = value.Split('|');
            return parts[0] switch
            {
                "DEMO-CABLE-LOT" => new LabelScanDto("IT", [value], 21, value, "Demo Cable Reel (lot)"),
                "DEMO-BOLT-10" => new LabelScanDto("IT", [value], 11, value, "Demo Hex Bolt M10"),
                "DEMO-FREIGHT" => new LabelScanDto("IT", [value], 13, value, "Demo Freight Service"),
                "LC" => new LabelScanDto("LC", parts[1..], null, null, null),
                "LT" or "PL" => new LabelScanDto(
                    parts[0], parts[1..], parts.Contains("DEMO-CABLE-LOT") ? 21 : 11, parts.Contains("DEMO-CABLE-LOT") ? "DEMO-CABLE-LOT" : "DEMO-BOLT-10", null),
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
