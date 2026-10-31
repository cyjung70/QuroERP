using System.Collections.Concurrent;
using System.Globalization;

namespace QuroErp.Web.Common;

/// <summary>
/// The one display culture factory (i18n.md "숫자·날짜"): date and time look the same on every server, whatever its
/// ICU or Windows culture data. ko 2026-10-30 오전 9:00; en and every other language 10/30/2026 9:00 AM with a normal space.
/// Grids and Quro.UI controls get the same culture.
/// </summary>
public static class DisplayCulture
{
    private static readonly ConcurrentDictionary<string, CultureInfo> Cultures = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The display culture of the user language (the UI culture).</summary>
    public static CultureInfo Current => For(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);

    /// <summary>The read-only display culture of a language code; a language without its own row uses the en patterns.</summary>
    public static CultureInfo For(string? lang) => Cultures.GetOrAdd(string.IsNullOrWhiteSpace(lang) ? "en" : lang, Create);

    /// <summary>A date in the display culture's date pattern.</summary>
    public static string FormatDate(DateOnly date, CultureInfo? culture = null) => FormatDate(date.ToDateTime(TimeOnly.MinValue), culture);

    /// <summary>The date part of a time in the display culture's date pattern.</summary>
    public static string FormatDate(DateTime at, CultureInfo? culture = null)
    {
        var display = culture ?? Current;
        return at.ToString(display.DateTimeFormat.ShortDatePattern, display);
    }

    /// <summary>Date and time without seconds in the display culture's patterns.</summary>
    public static string FormatDateTime(DateTime at, CultureInfo? culture = null)
    {
        var display = culture ?? Current;
        var format = display.DateTimeFormat;
        return at.ToString($"{format.ShortDatePattern} {format.ShortTimePattern}", display);
    }

    private static CultureInfo Create(string lang)
    {
        var ko = string.Equals(lang, "ko", StringComparison.OrdinalIgnoreCase);
        CultureInfo culture;
        try
        {
            culture = (CultureInfo)CultureInfo.GetCultureInfo(lang).Clone();
        }
        catch (CultureNotFoundException)
        {
            culture = (CultureInfo)CultureInfo.GetCultureInfo("en").Clone();
        }
        var format = culture.DateTimeFormat;
        if (format.Calendar is not GregorianCalendar
            && culture.OptionalCalendars.OfType<GregorianCalendar>().FirstOrDefault() is { } gregorian)
        {
            format.Calendar = gregorian;
        }
        // "/" and ":" in a pattern take these separators, so they are set too, not left to the host data
        format.DateSeparator = ko ? "-" : "/";
        format.TimeSeparator = ":";
        format.ShortDatePattern = ko ? "yyyy-MM-dd" : "MM/dd/yyyy";
        format.ShortTimePattern = ko ? "tt h:mm" : "h:mm tt";
        format.AMDesignator = ko ? "오전" : "AM";
        format.PMDesignator = ko ? "오후" : "PM";
        return CultureInfo.ReadOnly(culture);
    }
}
