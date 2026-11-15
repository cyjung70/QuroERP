using FluentAssertions;
using QuroErp.Domain.Mm.Labels;
using QuroErp.Infrastructure.Mm.Labels;
using UglyToad.PdfPig;

namespace QuroErp.Infrastructure.Tests.Reports;

/// <summary>The label format preview as a real PDF with Chromium (MM-UC-036-T03, MM-BR-917, MM-BR-918).</summary>
public class LabelPdfRendererTests(PdfRendererFixture fixture) : IClassFixture<PdfRendererFixture>
{
    private const double PointsPerMm = 72 / 25.4;

    [Theory, Trait("Id", "MM-UC-036-T03"), Trait("Rule", "MM-BR-918")]
    [InlineData(LabelType.Pallet, 100, 150, 2)]
    [InlineData(LabelType.Location, 100, 50, 1)]
    public async Task SampleLabel_PrintsPagesOfTheFormatSize_WithTheFields(string labelType, int width, int height, int pages)
    {
        var format = new LabelFormat(labelType, LabelType.Pdf, width, height, 203, LabelFormatRules.FieldsOf(labelType));
        var renderer = new LabelPdfRenderer(fixture.Renderer);

        var pdf = await renderer.RenderAsync(format, LabelPages.For([LabelFormatRules.Sample(labelType)]), 1, CancellationToken.None);

        using var document = PdfDocument.Open(pdf);
        document.NumberOfPages.Should().Be(pages);
        var page = document.GetPage(1);
        page.Width.Should().BeApproximately(width * PointsPerMm, 2.0);
        page.Height.Should().BeApproximately(height * PointsPerMm, 2.0);
        var text = page.Text.Replace(" ", "", StringComparison.Ordinal);
        text.Should().Contain(labelType == LabelType.Pallet ? "PL2611000001" : "A-01-01");
    }
}
