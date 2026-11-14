using System.Globalization;
using System.Text;
using QuroErp.Domain.Mm.Labels;

namespace QuroErp.Application.Mm.Labels;

/// <summary>
/// Zebra ZPL text of labels (MM-BR-919): one <c>^XA … ^XZ</c> block per page, sized from the format's mm and dpi. Text is
/// UTF-8 (<c>^CI28</c>) and every value goes through <c>^FH</c> hex escaping, so <c>^</c>, <c>~</c> and <c>_</c> in data
/// cannot become commands. The DataMatrix is the printer's own <c>^BX</c> (ECC 200).
/// </summary>
public static class ZplLabelWriter
{
    private const decimal MarginMm = 3m;
    private const decimal TextMm = 3.5m;

    public static string Write(LabelFormat format, IReadOnlyList<LabelPage> pages, int copies)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(copies);
        var zpl = new StringBuilder();
        foreach (var page in pages)
        {
            var margin = Dots(MarginMm, format.Dpi);
            var text = Dots(TextMm, format.Dpi);
            zpl.Append("^XA^CI28")
                .Append(CultureInfo.InvariantCulture, $"^PW{Dots(format.WidthMm, format.Dpi)}^LL{Dots(format.HeightMm, format.Dpi)}");
            var y = margin;
            if (page.Text)
            {
                foreach (var field in format.Fields)
                {
                    var value = page.Label.Values.GetValueOrDefault(field);
                    if (string.IsNullOrEmpty(value))
                    {
                        continue;
                    }
                    zpl.Append(CultureInfo.InvariantCulture, $"^FO{margin},{y}^A0N,{text},{text}^FH_^FD{Escape(value)}^FS");
                    y += text * 13 / 10;
                }
            }
            if (page.Barcode)
            {
                // A barcode-only page uses a bigger module so the scanner reads it from further away
                var module = Math.Max(2, Dots(page.Text ? 0.5m : 1m, format.Dpi));
                zpl.Append(CultureInfo.InvariantCulture, $"^FO{margin},{y}^BXN,{module},200^FH_^FD{Escape(page.Label.Code)}^FS");
            }
            zpl.Append(CultureInfo.InvariantCulture, $"^PQ{copies}^XZ\n");
        }
        return zpl.ToString();
    }

    /// <summary>mm at dpi, rounded to whole printer dots.</summary>
    public static int Dots(decimal mm, int dpi) => (int)Math.Round(mm / 25.4m * dpi, MidpointRounding.AwayFromZero);

    // ^FH_ reads _XX as one byte in hex; the UTF-8 bytes of the special characters are single bytes
    private static string Escape(string value)
    {
        var escaped = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            escaped.Append(c switch
            {
                '_' => "_5F",
                '^' => "_5E",
                '~' => "_7E",
                _ => c.ToString(),
            });
        }
        return escaped.ToString();
    }
}
