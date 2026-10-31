using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Screens;

namespace QuroErp.Web.Tests.Common;

/// <summary>Shown times in the company time zone, one formatter for the web (TASK-CORE-024, BC-BR-318).</summary>
public class CompanyTimeTests : BunitContext
{
    // 2026-10-28 09:30 UTC: 05:30 in New York (EDT), 18:30 in Seoul
    private static readonly DateTimeOffset SignedIn = new(2026, 10, 28, 9, 30, 0, TimeSpan.Zero);

    public CompanyTimeTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("en");
        Services.AddWebLocalization();
    }

    [Theory, Trait("Id", "CORE-024-WT-03"), Trait("Rule", "BC-BR-318")]
    [InlineData("America/New_York", 5)]
    [InlineData("Asia/Seoul", 18)]
    public void Format_ShowsAUtcTimeInTheZone(string timeZone, int hour)
    {
        CompanyTime.Format(SignedIn, CompanyTime.Find(timeZone)).Should().Be(Shown(hour));
    }

    [Theory, Trait("Id", "CORE-024-WT-04"), Trait("Rule", "BC-BR-318")]
    [InlineData("America/New_York", 5)]
    [InlineData("Asia/Seoul", 18)]
    public void AuditFooter_ShowsTimesInTheCompanyTimeZone(string timeZone, int hour)
    {
        Services.AddCompanyTime(timeZone);

        var footer = Render<QuroAuditFooter>(p => p.Add(x => x.Audit, new QuroAudit("kim", SignedIn, "lee", SignedIn)));

        footer.WaitForAssertion(() => footer.Find("dd").TextContent.Should().Be($"{Shown(hour)} kim"));
    }

    // 2026-10-28 hh:30 in the en culture's short date and time. Built from the culture, not hard-coded:
    // ICU versions differ in the space before AM/PM (U+202F on Linux), and the hour is what the test checks.
    private static string Shown(int hour) => new DateTime(2026, 10, 28, hour, 30, 0).ToString("g", CultureInfo.CurrentCulture);

    [Fact, Trait("Id", "CORE-024-WT-05"), Trait("Rule", "BC-BR-318")]
    public async Task GetZone_ReadsOnce_UntilReset()
    {
        var reads = 0;
        var time = new CompanyTime(_ => { reads++; return Task.FromResult("Asia/Seoul"); }, new MemoryCache(new MemoryCacheOptions()));

        (await time.GetZoneAsync(CancellationToken.None)).Id.Should().Be("Asia/Seoul");
        await time.GetZoneAsync(CancellationToken.None);
        reads.Should().Be(1);

        time.Reset();
        await time.GetZoneAsync(CancellationToken.None);
        reads.Should().Be(2);
    }

    [Fact, Trait("Id", "CORE-024-WT-06"), Trait("Rule", "BC-BR-318")]
    public void TimeZoneOptions_AreIanaNames_WithTheirOffset()
    {
        CompanyTime.TimeZoneOptions.Should().Contain(o => o.Code == "Asia/Seoul" && o.Name == "(UTC+09:00) Asia/Seoul")
            .And.Contain(o => o.Code == "America/New_York" && o.Name == "(UTC-05:00) America/New_York")
            .And.OnlyContain(o => TimeZoneInfo.FindSystemTimeZoneById(o.Code).HasIanaId);
    }
}
