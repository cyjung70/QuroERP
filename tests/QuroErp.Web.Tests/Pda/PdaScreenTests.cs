using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Contracts.Mm.Labels;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Layout;
using QuroErp.Web.Components.Pda;
using QuroErp.Web.Mm.Labels;

namespace QuroErp.Web.Tests.Pda;

/// <summary>PDA layout, frame and scan input (TASK-CORE-071, screens.md "PDA 화면", MM-BR-918, MM-BR-923) against a fake scan API.</summary>
public class PdaScreenTests : BunitContext
{
    private readonly FakeScanApi _api = new();
    private readonly BunitAuthorizationContext _auth;

    public PdaScreenTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("ko");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddWebLocalization();
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton<AccessTokenStore>();
        Services.AddScoped<AccessTokenProvider>();
        Services.AddHttpClient<AuthApiClient>(Configure);
        Services.AddHttpClient<LabelScanApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddScoped<IScreenAccess, ScreenAccess>();
        _auth = AddAuthorization();
        SignIn("admin");
    }

    [Fact, Trait("Id", "CORE-071-WT-01"), Trait("Rule", "MM-BR-918")]
    public void Enter_ConfirmsTheScan_TheReadLabelGoesToTheScreen_AndTheInputIsClearedForTheNext()
    {
        var scanned = new List<LabelScanDto>();
        var input = ScanInput(scanned, [LabelKinds.Pallet, LabelKinds.Lot]);

        Scan(input, "PL|PL2611000001|BOLT|261104-001|12|");

        input.WaitForAssertion(() => scanned.Should().ContainSingle());
        scanned[0].Should().BeEquivalentTo(new LabelScanDto("PL", ["PL2611000001", "BOLT", "261104-001", "12", ""], 7, "BOLT", "Hex Bolt"));
        _api.Queries.Should().Equal("/api/mm/label-scans?value=PL%7CPL2611000001%7CBOLT%7C261104-001%7C12%7C&accept=PL&accept=LT");
        input.Find("[data-testid=scan-input]").GetAttribute("value").Should().BeEmpty();
        input.FindAll("[data-testid=scan-error]").Should().BeEmpty();
        JSInterop.Invocations["quroPda.reject"].Should().BeEmpty();
    }

    [Fact, Trait("Id", "CORE-071-WT-02")]
    public void Typing_WithoutEnter_ReadsNothing_AndTheInputKeepsTheFocus()
    {
        var scanned = new List<LabelScanDto>();
        var input = ScanInput(scanned, []);

        input.Find("[data-testid=scan-input]").Input("BOL");

        _api.Queries.Should().BeEmpty();
        JSInterop.Invocations["quroPda.keepFocus"].Should().ContainSingle("the input takes the focus and takes it back after a tap elsewhere");
    }

    [Fact, Trait("Id", "CORE-071-WT-03"), Trait("Rule", "MM-BR-923")]
    public void UnknownValue_IsRejected_WithARedMessage_SoundAndVibration()
    {
        var scanned = new List<LabelScanDto>();
        var input = ScanInput(scanned, []);

        Scan(input, "NOPE");

        input.WaitForAssertion(() => input.Find("[data-testid=scan-error]").TextContent.Should().Be("읽을 수 없는 바코드입니다: NOPE"));
        input.Find(".quro-pda-scan").ClassList.Should().Contain("is-rejected");
        input.Find("[data-testid=scan-input]").GetAttribute("aria-invalid").Should().Be("true");
        JSInterop.Invocations["quroPda.reject"].Should().ContainSingle();
        scanned.Should().BeEmpty();
    }

    [Fact, Trait("Id", "CORE-071-WT-04"), Trait("Rule", "MM-BR-923")]
    public void AKindTheStepDoesNotTake_IsRejected_ThenAGoodScanClearsTheMessage()
    {
        var scanned = new List<LabelScanDto>();
        var input = ScanInput(scanned, [LabelKinds.Pallet]);

        Scan(input, "LC|MAIN|A01");
        input.WaitForAssertion(() => input.Find("[data-testid=scan-error]").TextContent.Should().Be("읽을 수 없는 바코드입니다: LC|MAIN|A01"));
        Scan(input, "PL|PL2611000001|BOLT|261104-001|12|");

        input.WaitForAssertion(() => scanned.Should().ContainSingle());
        input.FindAll("[data-testid=scan-error]").Should().BeEmpty();
        JSInterop.Invocations["quroPda.reject"].Should().ContainSingle();
    }

    [Fact, Trait("Id", "CORE-071-WT-05"), Trait("Rule", "MM-BR-923")]
    public void TheScreen_CanRejectAReadValue_ForItsOwnReason()
    {
        var input = ScanInput([], []);

        input.InvokeAsync(() => input.Instance.RejectAsync("발주에 없는 품목입니다."));

        input.Find("[data-testid=scan-error]").TextContent.Should().Be("발주에 없는 품목입니다.");
        JSInterop.Invocations["quroPda.reject"].Should().ContainSingle();
    }

    [Fact, Trait("Id", "CORE-071-WT-06"), Trait("Rule", "BC-BR-214")]
    public void Frame_HasHeaderScanCardsAndSaveInOrder_SaveNeedsLinesAndWriteAccess()
    {
        var saves = 0;
        IRenderedComponent<QuroPdaScreen> Frame(int lines) => Render<QuroPdaScreen>(p => p
            .Add(x => x.Title, "PDA 입고")
            .Add(x => x.WritePath, "/mm/goods-receipt")
            .Add(x => x.LineCount, lines)
            .Add(x => x.OnSave, () => saves++)
            .Add(x => x.Header, (RenderFragment)(b => b.AddMarkupContent(0, "<div class=\"quro-pda-field\">PO-001</div>")))
            .Add(x => x.Scan, (RenderFragment)(b => b.AddMarkupContent(0, "<div class=\"quro-pda-scan\"></div>")))
            .Add(x => x.Lines, (RenderFragment)(b => b.AddMarkupContent(0, "<article class=\"quro-pda-card\">BOLT</article>")))
            .Add(x => x.Actions, (RenderFragment)(b => b.AddMarkupContent(0, "<button type=\"button\">Location 바꾸기</button>"))));

        var empty = Frame(0);
        var full = Frame(1);

        empty.Find("[data-testid=pda-save]").HasAttribute("disabled").Should().BeTrue("there is nothing to save");
        empty.Find("[data-testid=pda-lines]").TextContent.Should().Be("스캔한 줄이 없습니다.");
        full.FindAll(".quro-pda-screen > *").Select(e => e.ClassName ?? e.TagName.ToLowerInvariant()).Should().Equal(
            "quro-pda-head", "quro-pda-scan", "quro-pda-lines", "quro-pda-actions");
        full.FindAll(".quro-pda-actions > button").Select(b => b.TextContent).Should().Equal("Location 바꾸기", "저장");
        full.Find("[data-testid=pda-save]").Click();
        saves.Should().Be(1);
    }

    [Fact, Trait("Id", "CORE-071-WT-06"), Trait("Rule", "BC-BR-214")]
    public void Frame_WithoutWriteAccess_KeepsSaveOff()
    {
        SignIn("user");

        var frame = Render<QuroPdaScreen>(p => p.Add(x => x.Title, "PDA").Add(x => x.WritePath, "/mm/goods-receipt").Add(x => x.LineCount, 2));

        frame.Find("[data-testid=pda-save]").HasAttribute("disabled").Should().BeTrue();
    }

    // bUnit has no layout engine, so the 360px rules are read from the stylesheet: one centered column of 360px, touch targets of
    // 44px, text of 16px, and a bottom bar whose buttons share the width and wrap their text instead of being cut off
    [Fact, Trait("Id", "CORE-071-WT-07")]
    public void Css_OneColumnOf360px_44pxTargets_16pxText_AndBottomButtonsThatWrapInsteadOfBeingCut()
    {
        var css = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "QuroErp.Web", "wwwroot", "css", "quro-pda.css"));

        Rule(css, ".quro-pda {").Should().Contain("--q-pda-w: 360px").And.Contain("--q-pda-touch: 44px").And.Contain("font-size: 16px");
        Rule(css, ".quro-pda-body {").Should().Contain("max-width: var(--q-pda-w)");
        Rule(css, ".quro-pda a {").Should().Contain("min-height: var(--q-pda-touch)").And.Contain("font-size: 16px");
        Rule(css, ".quro-pda-actions {").Should().Contain("position: sticky").And.Contain("bottom: 0").And.Contain("flex-wrap: wrap");
        Rule(css, ".quro-pda-actions > button {").Should().Contain("min-width: 0").And.Contain("white-space: normal")
            .And.Contain("overflow-wrap: anywhere").And.NotMatchRegex(@"(?m)^\s*(width|max-width):").And.NotContain("overflow: hidden")
            .And.NotContain("nowrap");
        css.Should().NotContain("quro-grid", "PDA screens use no grid");
    }

    [Fact, Trait("Id", "CORE-071-WT-08")]
    public void PdaPages_AreNotOpenedAsScreenTabs()
    {
        ShellRoutes.Resolve("/samples/pda").Should().BeNull("a PDA page has its own layout, without the shell");
        ShellRoutes.Resolve("/samples/single").Should().NotBeNull();
    }

    private IRenderedComponent<QuroScanInput> ScanInput(List<LabelScanDto> scanned, IReadOnlyList<string> accept) =>
        Render<QuroScanInput>(p => p
            .Add(x => x.Accept, accept)
            .Add(x => x.TestId, "scan")
            .Add(x => x.OnScanned, (LabelScanDto scan) => scanned.Add(scan)));

    private static void Scan(IRenderedComponent<QuroScanInput> input, string value)
    {
        input.Find("[data-testid=scan-input]").Input(value);
        input.Find("[data-testid=scan-input]").KeyDown(new KeyboardEventArgs { Key = "Enter" });
    }

    private void SignIn(string role)
    {
        var store = Services.GetRequiredService<AccessTokenStore>();
        var sessionId = store.Add(new LoginResponse($"token-{role}", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", role, false, "ko"));
        _auth.SetAuthorized("kim");
        _auth.SetRoles(role);
        _auth.SetClaims(new Claim(WebAuthClaims.SessionId, sessionId));
    }

    private static string Rule(string css, string selector)
    {
        var start = css.IndexOf(selector, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"{selector} needs its own rule");
        return css[start..css.IndexOf('}', start)];
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "QuroErp.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new DirectoryNotFoundException("QuroErp.slnx not found");
    }

    private static void Configure(HttpClient http) => http.BaseAddress = new Uri("http://api.test");

    // Reads like the scan API: PL with item BOLT, LC without an item, anything else or a kind outside accept is MM-E-923
    private sealed class FakeScanApi : HttpMessageHandler
    {
        public List<string> Queries { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Queries.Add(uri.PathAndQuery);
            var query = uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).ToList();
            var value = Uri.UnescapeDataString(query.First(p => p[0] == "value")[1]);
            var accept = query.Where(p => p[0] == "accept").Select(p => p[1]).ToList();
            var parts = value.Split('|');
            LabelScanDto? scan = parts[0] switch
            {
                "PL" when parts.Length == 6 => new LabelScanDto("PL", parts[1..], 7, "BOLT", "Hex Bolt"),
                "LC" when parts.Length == 3 => new LabelScanDto("LC", parts[1..], null, null, null),
                _ => null,
            };
            if (scan is null || (accept.Count > 0 && !accept.Contains(scan.Kind)))
            {
                var problem = JsonContent.Create(new { status = 422, code = "MM-E-923", args = new[] { value } });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.UnprocessableEntity) { Content = problem });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(scan) });
        }
    }
}
