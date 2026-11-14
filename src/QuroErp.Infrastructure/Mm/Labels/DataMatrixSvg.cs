using System.Globalization;
using System.Text;
using ZXing;
using ZXing.Datamatrix;
using ZXing.Datamatrix.Encoder;

namespace QuroErp.Infrastructure.Mm.Labels;

/// <summary>
/// A DataMatrix (ECC 200, square) as inline SVG for PDF labels (MM-BR-918). ZXing.Net only encodes the module pattern; the SVG is
/// one path of unit squares, so it needs no image, script or network request in the PDF engine.
/// </summary>
public static class DataMatrixSvg
{
    /// <param name="content">The label code (<see cref="QuroErp.Domain.Mm.Labels.LabelCode"/>).</param>
    /// <param name="sizeMm">The printed width and height of the symbol.</param>
    public static string Render(string content, decimal sizeMm)
    {
        var matrix = new DataMatrixWriter().encode(
            content, BarcodeFormat.DATA_MATRIX, 0, 0,
            new Dictionary<EncodeHintType, object> { [EncodeHintType.DATA_MATRIX_SHAPE] = SymbolShapeHint.FORCE_SQUARE });
        var path = new StringBuilder();
        for (var y = 0; y < matrix.Height; y++)
        {
            for (var x = 0; x < matrix.Width; x++)
            {
                if (matrix[x, y])
                {
                    path.Append(CultureInfo.InvariantCulture, $"M{x} {y}h1v1h-1z");
                }
            }
        }
        var size = sizeMm.ToString(CultureInfo.InvariantCulture);
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{size}mm\" height=\"{size}mm\" viewBox=\"0 0 {matrix.Width} {matrix.Height}\" "
            + $"shape-rendering=\"crispEdges\"><path d=\"{path}\" fill=\"#000\"/></svg>";
    }
}
