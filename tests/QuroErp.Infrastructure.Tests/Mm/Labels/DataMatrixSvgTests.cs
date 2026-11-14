using System.Globalization;
using System.Text.RegularExpressions;
using FluentAssertions;
using QuroErp.Domain.Mm.Labels;
using QuroErp.Infrastructure.Mm.Labels;
using ZXing;
using ZXing.Common;
using ZXing.Datamatrix;

namespace QuroErp.Infrastructure.Tests.Mm.Labels;

/// <summary>The PDF label DataMatrix is a square symbol that reads back as the label code (MM-BR-918).</summary>
public partial class DataMatrixSvgTests
{
    [Fact, Trait("Id", "MM-UT-918"), Trait("Rule", "MM-BR-918")]
    public void Render_GivesASquareSvgThatDecodesToTheLabelCode()
    {
        var code = LabelCode.ForPallet("PL2610000001", "A-100", "261010-001", 24.5m, new DateOnly(2027, 3, 31));

        var svg = DataMatrixSvg.Render(code, 25m);

        svg.Should().StartWith("<svg").And.Contain("width=\"25mm\" height=\"25mm\"");
        var viewBox = ViewBox().Match(svg);
        viewBox.Success.Should().BeTrue();
        viewBox.Groups[1].Value.Should().Be(viewBox.Groups[2].Value, "the symbol is forced square");
        Decode(svg, int.Parse(viewBox.Groups[1].Value, CultureInfo.InvariantCulture)).Should().Be(code);
    }

    // Draws the path squares into a grey image with a quiet zone and reads it with the ZXing DataMatrix reader
    private static string? Decode(string svg, int modules)
    {
        const int scale = 4, quiet = 2;
        var size = (modules + quiet * 2) * scale;
        var pixels = Enumerable.Repeat((byte)255, size * size).ToArray();
        foreach (Match square in Square().Matches(svg))
        {
            var x = (int.Parse(square.Groups[1].Value, CultureInfo.InvariantCulture) + quiet) * scale;
            var y = (int.Parse(square.Groups[2].Value, CultureInfo.InvariantCulture) + quiet) * scale;
            for (var dy = 0; dy < scale; dy++)
            {
                Array.Fill(pixels, (byte)0, (y + dy) * size + x, scale);
            }
        }
        var bitmap = new BinaryBitmap(new HybridBinarizer(new RGBLuminanceSource(pixels, size, size, RGBLuminanceSource.BitmapFormat.Gray8)));
        return new DataMatrixReader().decode(bitmap)?.Text;
    }

    [GeneratedRegex("viewBox=\"0 0 (\\d+) (\\d+)\"")]
    private static partial Regex ViewBox();

    [GeneratedRegex("M(\\d+) (\\d+)h1v1h-1z")]
    private static partial Regex Square();
}
