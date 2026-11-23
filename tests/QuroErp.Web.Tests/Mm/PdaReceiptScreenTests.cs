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
using QuroErp.Contracts.Common.Auth;
using QuroErp.Contracts.Mm.GoodsReceipts;
using QuroErp.Contracts.Mm.Labels;
using QuroErp.Contracts.Mm.Locations;
using QuroErp.Contracts.Mm.Warehouses;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Layout;
using QuroErp.Web.Components.Pages.Mm;
using QuroErp.Web.Components.Screens;
using QuroErp.Web.Mm.GoodsReceipts;
using QuroErp.Web.Mm.Labels;
using QuroErp.Web.Mm.Locations;
using QuroErp.Web.Mm.Warehouses;
using QuroErp.Web.Tests.Screens;

namespace QuroErp.Web.Tests.Mm;

/// <summary>
/// PDA receipt /mm/pda-receipt (MM-UC-038, TASK-MM-060) against a fake API: PO DEMO-PO-PDA has a lot- and Location-managed
/// resin line (item 12) and a bolt line (item 11) in warehouse MAIN, whose Locations are '*' and A1.
/// </summary>
public class PdaReceiptScreenTests : BunitContext
{
    private const string Po = "DEMO-PO-PDA";
    private readonly FakePdaApi _api = new();
    private readonly FakeConfirm _confirm = new();
    private readonly BunitAuthorizationContext _auth;

    public PdaReceiptScreenTests()
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
        Services.AddHttpClient<GoodsReceiptApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<LocationApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<WarehouseApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddScoped<IScreenAccess, ScreenAccess>();
        _auth = AddAuthorization();
    }

    [Fact, Trait("Id", "MM-UC-038-T01"), Trait("Rule", "MM-BR-920")]
    public void ScanningThePoNumber_ShowsTheVendorAndTheLinesLeft_ThenAsksForItems()
    {
        var page = RenderAs("admin");
        page.Find("[data-testid=pda-receipt-scan-input]").GetAttribute("placeholder").Should().NotBeNull();
        page.Find(".quro-pda-scan-label").TextContent.Should().Be("발주 번호를 스캔하세요");

        Scan(page, "demo-po-pda");

        page.WaitForAssertion(() => page.Find("[data-testid=pda-receipt-po]").TextContent.Should().Be(Po));
        page.Find("[data-testid=pda-receipt-vendor]").TextContent.Should().Be("MAPLE LEAF SUPPLY");
        page.FindAll("[data-testid=pda-receipt-open-line]").Select(l => l.TextContent)
            .Should().Equal("1 · DEMO-RESIN-LOC · 100 KG", "2 · DEMO-BOLT-10 · 500 EA");
        page.Find(".quro-pda-scan-label").TextContent.Should().Be("품목을 스캔하세요");
        _api.Queries.Should().Equal("/api/mm/goods-receipts/open-po-lines?poNo=demo-po-pda");
    }

    [Fact, Trait("Id", "MM-UC-038-T02"), Trait("Rule", "MM-BR-923")]
    public void AnUnknownPo_AnUnknownBarcode_AndAnItemNotOnThePo_AreRejected()
    {
        var page = RenderAs("admin");

        Scan(page, "PO-NONE");
        page.WaitForAssertion(() => Error(page).Should().Be("읽을 수 없는 바코드입니다: PO-NONE"));
        page.Find("[data-testid=pda-receipt-po]").TextContent.Should().Be("-");

        Scan(page, Po);
        page.WaitForAssertion(() => page.Find("[data-testid=pda-receipt-po]").TextContent.Should().Be(Po));
        Scan(page, "NOPE");
        page.WaitForAssertion(() => Error(page).Should().Be("읽을 수 없는 바코드입니다: NOPE"));
        Scan(page, "DEMO-FREIGHT");
        page.WaitForAssertion(() => Error(page).Should().Be("읽을 수 없는 바코드입니다: DEMO-FREIGHT"));

        page.FindAll("[data-testid=pda-receipt-card]").Should().BeEmpty();
        JSInterop.Invocations["quroPda.reject"].Should().HaveCount(3);
    }

    [Fact, Trait("Id", "MM-UC-038-T03"), Trait("Rule", "MM-BR-920"), Trait("Rule", "MM-BR-910"), Trait("Rule", "MM-BR-353"),
     Trait("Rule", "MM-BR-364"), Trait("Rule", "MM-BR-713")]
    public void Save_SendsTheScannedLines_ToTheReceiptApi_ThenPrintsThePalletLabels()
    {
        var page = RenderAs("admin");
        Scan(page, Po);
        page.WaitForAssertion(() => page.Find("[data-testid=pda-receipt-po]").TextContent.Should().Be(Po));
        Scan(page, "DEMO-RESIN-LOC");
        page.WaitForAssertion(() => page.FindAll("[data-testid=pda-receipt-card]").Should().HaveCount(1));
        page.Find(".quro-pda-scan-label").TextContent.Should().Be("품목 또는 받는 Location을 스캔하세요");
        Scan(page, "LC|MAIN|A1");
        page.WaitForAssertion(() => page.Find("[data-testid=pda-receipt-location]").GetAttribute("value").Should().Be("A1"));
        page.Find("[data-testid=pda-receipt-qty]").Change("40");
        page.Find("[data-testid=pda-receipt-pallet-qty]").Change("15");
        page.Find("[data-testid=pda-receipt-supplier-lot]").Change(" SUP-9 ");
        page.Find("[data-testid=pda-receipt-expiry]").Change("2027-06-30");
        Scan(page, "DEMO-BOLT-10");
        page.WaitForAssertion(() => page.FindAll("[data-testid=pda-receipt-card]").Should().HaveCount(2));

        page.Find("[data-testid=pda-receipt-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=pda-receipt-notice]").TextContent.Should().Be("입고 GR2611000009을(를) 저장했습니다."));
        var request = _api.Saved.Should().ContainSingle().Subject;
        (request.VendorId, request.WarehouseId, request.ConfirmOverReceipt).Should().Be((5L, (long?)1, (bool?)null));
        request.Lines.Select(l => (l.ItemId, l.GrQty, l.PurchaseOrderLineId, l.LocationId, l.PalletQty, l.SupplierLotNo, l.ExpiryDt))
            .Should().Equal(
                (12L, 40m, (long?)101, (long?)31, (decimal?)15m, "SUP-9", (DateOnly?)new DateOnly(2027, 6, 30)),
                (11L, 500m, (long?)102, (long?)null, (decimal?)null, (string?)null, (DateOnly?)null));
        page.FindAll("[data-testid=pda-receipt-card]").Should().BeEmpty("the next PO starts empty");
        page.Find("[data-testid=pda-receipt-po]").TextContent.Should().Be("-");

        page.Find("[data-testid=pda-receipt-labels]").HasAttribute("disabled").Should().BeFalse();
        page.Find("[data-testid=pda-receipt-labels]").Click();
        page.WaitForAssertion(() => _api.Queries.Should().Contain("/api/mm/goods-receipts/77/pallet-labels"));
    }

    [Fact, Trait("Id", "MM-UC-038-T02"), Trait("Rule", "MM-BR-713"), Trait("Rule", "MM-BR-363")]
    public void LotFields_OnlyForLotItems_ALocationOfAnotherWarehouseIsRejected_AndALocationNeedsALocationItem()
    {
        var page = RenderAs("admin");
        Scan(page, Po);
        page.WaitForAssertion(() => page.Find("[data-testid=pda-receipt-po]").TextContent.Should().Be(Po));
        Scan(page, "DEMO-BOLT-10");
        page.WaitForAssertion(() => page.FindAll("[data-testid=pda-receipt-card]").Should().HaveCount(1));

        page.FindAll("[data-testid=pda-receipt-supplier-lot]").Should().BeEmpty();
        page.Find("[data-testid=pda-receipt-location]").GetAttribute("value").Should().Be("*");
        Scan(page, "LC|MAIN|A1");
        // The bolt is not Location-managed, so the step does not take a Location label
        page.WaitForAssertion(() => Error(page).Should().Be("읽을 수 없는 바코드입니다: LC|MAIN|A1"));

        Scan(page, "DEMO-RESIN-LOC");
        page.WaitForAssertion(() => page.FindAll("[data-testid=pda-receipt-card]").Should().HaveCount(2));
        page.FindAll("[data-testid=pda-receipt-supplier-lot]").Should().ContainSingle();
        Scan(page, "LC|SECOND|A1");
        page.WaitForAssertion(() => Error(page).Should().Be("Location SECOND / A1은(는) 이 줄의 창고 MAIN에 없습니다."));
        page.FindAll("[data-testid=pda-receipt-location]")[1].GetAttribute("value").Should().Be("*");
    }

    [Fact, Trait("Id", "MM-UC-038-T07"), Trait("Rule", "MM-BR-102")]
    public void OverReceipt_AsksLikeThePcScreen_AndNoKeepsTheCards()
    {
        _api.SaveProblem = """{"status":422,"code":"MM-W-102","args":["DEMO-PO-PDA","2","500"],"confirmField":"confirmOverReceipt"}""";
        _confirm.Answer = false;
        var page = RenderAs("admin");
        Scan(page, Po);
        page.WaitForAssertion(() => page.Find("[data-testid=pda-receipt-po]").TextContent.Should().Be(Po));
        Scan(page, "DEMO-BOLT-10");
        page.WaitForAssertion(() => page.FindAll("[data-testid=pda-receipt-card]").Should().HaveCount(1));

        page.Find("[data-testid=pda-receipt-save]").Click();

        page.WaitForAssertion(() => _confirm.Questions.Should().ContainSingle().Which.Should().Contain("DEMO-PO-PDA"));
        page.Find("[data-testid=pda-receipt-error]").TextContent.Should().Be("저장하지 않았습니다. 입고 수량을 확인하세요.");
        page.FindAll("[data-testid=pda-receipt-card]").Should().HaveCount(1);
        _api.Saved.Should().ContainSingle();
    }

    [Fact, Trait("Id", "MM-UC-038-T05"), Trait("Rule", "BC-BR-214")]
    public void WithoutWriteAccessOnTheReceiptScreen_SaveStaysOff_AndDisabledButtonsAreShown()
    {
        var page = RenderAs("user");
        Scan(page, Po);
        page.WaitForAssertion(() => page.Find("[data-testid=pda-receipt-po]").TextContent.Should().Be(Po));
        Scan(page, "DEMO-BOLT-10");
        page.WaitForAssertion(() => page.FindAll("[data-testid=pda-receipt-card]").Should().HaveCount(1));

        page.Find("[data-testid=pda-receipt-save]").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=pda-receipt-labels]").HasAttribute("disabled").Should().BeTrue("shown, not hidden, before a save");
        page.FindAll(".quro-pda-actions > button").Select(b => b.TextContent.Trim()).Should().Equal("발주 바꾸기", "Pallet 라벨", "저장");
    }

    [Fact, Trait("Id", "MM-UC-038-T04")]
    public void TheCardFields_UseTheEntryColors_AndThePageIsAPdaPage()
    {
        var page = RenderAs("admin");
        Scan(page, Po);
        page.WaitForAssertion(() => page.Find("[data-testid=pda-receipt-po]").TextContent.Should().Be(Po));
        Scan(page, "DEMO-RESIN-LOC");
        page.WaitForAssertion(() => page.FindAll("[data-testid=pda-receipt-card]").Should().HaveCount(1));

        Field(page, "qty").ClassList.Should().Contain(["quro-field-input", "quro-field-required"]);
        Field(page, "qty").QuerySelector(".quro-required")!.TextContent.Should().Be("*");
        Field(page, "location").ClassList.Should().Contain("quro-field-readonly");
        Field(page, "pallet-qty").ClassList.Should().Contain("quro-field-input").And.NotContain("quro-field-required");
        ShellRoutes.Resolve("/mm/pda-receipt").Should().BeNull("a PDA page is not a screen tab");
    }

    private IRenderedComponent<PdaReceipt> RenderAs(string role)
    {
        var store = Services.GetRequiredService<AccessTokenStore>();
        var sessionId = store.Add(new LoginResponse($"token-{role}", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", role, false, "ko"));
        _auth.SetAuthorized("kim");
        _auth.SetRoles(role);
        _auth.SetClaims(new Claim(WebAuthClaims.SessionId, sessionId));
        return Render<PdaReceipt>();
    }

    private static void Scan(IRenderedComponent<PdaReceipt> page, string value)
    {
        page.Find("[data-testid=pda-receipt-scan-input]").Input(value);
        page.Find("[data-testid=pda-receipt-scan-input]").KeyDown(new KeyboardEventArgs { Key = "Enter" });
    }

    private static string Error(IRenderedComponent<PdaReceipt> page) => page.Find("[data-testid=pda-receipt-scan-error]").TextContent;

    private static AngleSharp.Dom.IElement Field(IRenderedComponent<PdaReceipt> page, string name) =>
        page.Find($"[data-testid=pda-receipt-{name}]").ParentElement!;

    private static void Configure(HttpClient http) => http.BaseAddress = new Uri("http://api.test");

    private sealed class FakePdaApi : HttpMessageHandler
    {
        private static readonly OpenPoLineDto Resin = new(
            101, 9, Po, 1, new DateOnly(2026, 11, 2), 12, "DEMO-RESIN-LOC", "Demo Resin (Lot, Location)", "KG", 1, 100m, 0m, 100m, 3.5m,
            0m, 4, "USD", 1m, 5, "MAPLE LEAF SUPPLY", true, true);

        private static readonly OpenPoLineDto Bolt = new(
            102, 9, Po, 2, new DateOnly(2026, 11, 2), 11, "DEMO-BOLT-10", "Demo Hex Bolt M10", "EA", 1, 500m, 0m, 500m, 0.42m,
            0m, 4, "USD", 1m, 5, "MAPLE LEAF SUPPLY", false, false);

        public List<string> Queries { get; } = [];

        public List<SaveGoodsReceiptRequest> Saved { get; } = [];

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
            if (path.EndsWith("/" + GoodsReceiptRoutes.OpenPoLines, StringComparison.Ordinal))
            {
                if (!string.Equals(query["poNo"], Po, StringComparison.OrdinalIgnoreCase))
                {
                    return Problem("MM-E-923", query["poNo"]);
                }
                OpenPoLineDto[] lines = [Resin, Bolt];
                if (query.TryGetValue("itemId", out var itemId))
                {
                    lines = [.. lines.Where(l => l.ItemId.ToString(CultureInfo.InvariantCulture) == itemId)];
                    if (lines.Length == 0)
                    {
                        return Problem("MM-E-923", "DEMO-FREIGHT");
                    }
                }
                return Ok(lines);
            }
            if (path == "/" + WarehouseRoutes.Options)
            {
                return Ok(new[] { new WarehouseOptionDto(1, "MAIN", "Main", "NORMAL", true), new WarehouseOptionDto(2, "SECOND", "Second", "NORMAL", true) });
            }
            if (path == "/" + LocationRoutes.Options)
            {
                return Ok(new[] { new LocationOptionDto(30, "*", null), new LocationOptionDto(31, "A1", "Shelf A1") });
            }
            if (path.EndsWith("/" + GoodsReceiptRoutes.PalletLabels, StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([0x25, 0x50, 0x44, 0x46]) };
            }
            if (request.Method == HttpMethod.Post && path == "/" + GoodsReceiptRoutes.Base)
            {
                var save = (await request.Content!.ReadFromJsonAsync<SaveGoodsReceiptRequest>(cancellationToken))!;
                Saved.Add(save);
                if (SaveProblem is not null && save.ConfirmOverReceipt is null)
                {
                    return new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
                    {
                        Content = new StringContent(SaveProblem, Encoding.UTF8, "application/problem+json"),
                    };
                }
                const string saved = """
                    {"id":77,"grNo":"GR2611000009","lines":[{"id":1,"seq":1,"palletNos":["PL2611000001","PL2611000002","PL2611000003"]},
                     {"id":2,"seq":2}]}
                    """;
                return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent(saved, Encoding.UTF8, "application/json") };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        // Like the scan API: item codes come back as IT, LC labels as LC; anything else or a kind outside accept is MM-E-923
        private static LabelScanDto? ReadLabel(string value) => value switch
        {
            "DEMO-RESIN-LOC" => new LabelScanDto("IT", [value], 12, value, "Demo Resin (Lot, Location)"),
            "DEMO-BOLT-10" => new LabelScanDto("IT", [value], 11, value, "Demo Hex Bolt M10"),
            "DEMO-FREIGHT" => new LabelScanDto("IT", [value], 13, value, "Demo Freight Service"),
            _ when value.StartsWith("LC|", StringComparison.Ordinal) => new LabelScanDto("LC", value.Split('|')[1..], null, null, null),
            _ => null,
        };

        private static HttpResponseMessage Ok<T>(T body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };

        private static HttpResponseMessage Problem(string code, string arg) => new(HttpStatusCode.UnprocessableEntity)
        {
            Content = JsonContent.Create(new { status = 422, code, args = new[] { arg } }),
        };
    }
}
