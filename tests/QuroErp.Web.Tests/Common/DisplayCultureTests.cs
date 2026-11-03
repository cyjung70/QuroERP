using FluentAssertions;
using QuroErp.Web.Common;

namespace QuroErp.Web.Tests.Common;

/// <summary>Fixed date and time display per user language, the same on every server (TASK-CORE-058, i18n.md "숫자·날짜").</summary>
public class DisplayCultureTests
{
    [Theory, Trait("Id", "CORE-058-WT-01")]
    [InlineData("ko", 9, "2026-10-30 오전 9:00")]
    [InlineData("ko", 21, "2026-10-30 오후 9:00")]
    [InlineData("en", 9, "10/30/2026 9:00 AM")]
    [InlineData("en", 21, "10/30/2026 9:00 PM")]
    public void FormatDateTime_UsesTheLanguagePatterns(string lang, int hour, string shown)
    {
        DisplayCulture.FormatDateTime(new DateTime(2026, 10, 30, hour, 0, 5), DisplayCulture.For(lang)).Should().Be(shown);
    }

    [Theory, Trait("Id", "CORE-058-WT-02")]
    [InlineData("ko", "2026-10-30")]
    [InlineData("en", "10/30/2026")]
    public void FormatDate_UsesTheLanguagePattern(string lang, string shown)
    {
        DisplayCulture.FormatDate(new DateOnly(2026, 10, 30), DisplayCulture.For(lang)).Should().Be(shown);
        DisplayCulture.FormatDate(new DateTime(2026, 10, 30, 21, 0, 0), DisplayCulture.For(lang)).Should().Be(shown);
    }

    [Theory, Trait("Id", "CORE-058-WT-03")]
    [InlineData("ja")]
    [InlineData("de")]
    [InlineData(null)]
    public void For_OtherLanguage_UsesTheEnPatterns(string? lang)
    {
        DisplayCulture.FormatDateTime(new DateTime(2026, 10, 30, 21, 0, 0), DisplayCulture.For(lang)).Should().Be("10/30/2026 9:00 PM");
    }

    [Fact, Trait("Id", "CORE-058-WT-04")]
    public void FormatDateTime_En_HasANormalSpaceBeforeAmPm()
    {
        var shown = DisplayCulture.FormatDateTime(new DateTime(2026, 10, 30, 9, 0, 0), DisplayCulture.For("en"));

        shown.Should().NotContain(" ").And.EndWith(" AM");
    }

    [Fact, Trait("Id", "CORE-058-WT-05")]
    public void For_IsReadOnly_AndTheSameInstancePerLanguage()
    {
        DisplayCulture.For("ko").IsReadOnly.Should().BeTrue();
        DisplayCulture.For("ko").Should().BeSameAs(DisplayCulture.For("KO"));
    }
}
