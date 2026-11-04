using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using QuroErp.Application.Common.Printing;
using QuroErp.Infrastructure.Reports;
using UglyToad.PdfPig;

namespace QuroErp.Infrastructure.Tests.Reports;

/// <summary>One Chromium for the PDF tests; it needs `playwright.ps1 install chromium` (CI installs it).</summary>
public sealed class PdfRendererFixture : IAsyncDisposable
{
    public PdfRenderer Renderer { get; } = new(new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance, new PrintOptions());

    public ValueTask DisposeAsync() => Renderer.DisposeAsync();
}

/// <summary>HTML template → PDF with real Chromium (reports.md "PDF", security.md "출력 인코딩").</summary>
public class PdfRendererTests(PdfRendererFixture fixture) : IClassFixture<PdfRendererFixture>
{
    private const string Script = "<script>alert(1)</script>";
    private static readonly PrintCompany Company = new("큐로 상사", ["서울시 중구 세종대로 1"], "02-000-0000", "office@example.com");

    [Fact, Trait("Id", "CORE-033-IT-01")]
    public async Task SamplePdf_HasKoreanTextNumberAndPageNumbers_PrintsScriptAsText_AndMakesNoRequest()
    {
        var before = fixture.Renderer.BlockedRequests;

        var pdf = await fixture.Renderer.RenderAsync(typeof(SamplePrint), Parameters(), PrintPaper.A4, CancellationToken.None);

        fixture.Renderer.BlockedRequests.Should().Be(before, "fonts and logo are data URIs and templates use no URL");
        using var document = PdfDocument.Open(pdf);
        var pages = document.GetPages().Select(p => Compact(p.Text)).ToList();
        pages.Should().HaveCountGreaterThan(1);
        pages[0].Should().Contain("큐로상사").And.Contain("발주서샘플").And.Contain("PO2611000001").And.Contain(Compact(Script))
            .And.Contain($"1/{pages.Count}");
        pages[^1].Should().Contain($"{pages.Count}/{pages.Count}").And.Contain("품목80");
    }

    [Theory, Trait("Id", "CORE-033-IT-02")]
    [InlineData("US", 612, 792)]
    [InlineData("KR", 595, 842)]
    public async Task Paper_FollowsTheCompanyCountry(string country, int width, int height)
    {
        var pdf = await fixture.Renderer.RenderAsync(typeof(SamplePrint), Parameters(lines: 1), PrintPaper.ForCountry(country), CancellationToken.None);

        using var document = PdfDocument.Open(pdf);
        var page = document.GetPage(1);
        // Points; Chromium's A4 is 8.27 x 11.7 inches, within a point of ISO 595 x 842
        page.Width.Should().BeApproximately(width, 1.0);
        page.Height.Should().BeApproximately(height, 1.0);
    }

    [Fact, Trait("Id", "CORE-033-IT-03")]
    public async Task ExternalUrl_IsBlocked_AndThePdfIsStillMade()
    {
        var before = fixture.Renderer.BlockedRequests;

        var pdf = await fixture.Renderer.RenderAsync(typeof(ExternalImageProbe), new Dictionary<string, object?>(), PrintPaper.A4, CancellationToken.None);

        fixture.Renderer.BlockedRequests.Should().BeGreaterThan(before);
        using var document = PdfDocument.Open(pdf);
        document.GetPage(1).Text.Should().Contain("probe");
    }

    [Fact]
    public async Task Check_StartsChromium()
    {
        var check = () => fixture.Renderer.CheckAsync(CancellationToken.None);

        await check.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NotAComponent_IsRejected()
    {
        var act = () => fixture.Renderer.RenderAsync(typeof(string), new Dictionary<string, object?>(), PrintPaper.A4, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private static Dictionary<string, object?> Parameters(int lines = 80) => new()
    {
        ["Company"] = Company,
        ["Title"] = "발주서 샘플",
        ["DocumentNo"] = "PO2611000001",
        ["Note"] = Script,
        ["Lines"] = Enumerable.Range(1, lines).Select(i => $"품목 {i}").ToList(),
        ["Culture"] = CultureInfo.GetCultureInfo("ko"),
    };

    // PDF text extraction splits words differently; compare without whitespace
    private static string Compact(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
}
