using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using QuroErp.Application.Common;

namespace QuroErp.Application.Tests.Common;

/// <summary>Records Today and Reset calls; today is a fixed date unless a test sets it.</summary>
internal sealed class FakeBusinessClock : IBusinessClock
{
    public TimeZoneInfo TimeZone { get; set; } = TimeZoneInfo.Utc;

    public DateOnly TodayValue { get; set; } = new(2026, 10, 30);

    public int Resets { get; private set; }

    public DateOnly Today() => TodayValue;

    public DateTimeOffset ToCompanyTime(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, TimeZone);

    public void Reset() => Resets++;
}

/// <summary>Business date and shown times in the company time zone (TASK-CORE-024, BC-BR-318).</summary>
public class BusinessClockTests
{
    private static readonly DateTimeOffset Utc0300 = new(2026, 1, 1, 3, 0, 0, TimeSpan.Zero);

    [Theory, Trait("Id", "CORE-024-UT-01"), Trait("Rule", "BC-BR-318")]
    [InlineData("America/New_York", 2025, 12, 31)]
    [InlineData("Asia/Seoul", 2026, 1, 1)]
    public void Today_IsTheDateInTheCompanyTimeZone(string timeZone, int year, int month, int day)
    {
        var clock = new BusinessClock(new FakeTimeProvider(Utc0300), () => timeZone);

        clock.Today().Should().Be(new DateOnly(year, month, day));
    }

    [Fact, Trait("Id", "CORE-024-UT-02"), Trait("Rule", "BC-BR-318")]
    public void Today_IgnoresTheServerTimeZone()
    {
        var time = new FakeTimeProvider(Utc0300);
        time.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul"));

        new BusinessClock(time, () => "America/New_York").Today().Should().Be(new DateOnly(2025, 12, 31));
    }

    [Fact, Trait("Id", "CORE-024-UT-03"), Trait("Rule", "BC-BR-318")]
    public void ToCompanyTime_ConvertsAUtcTime()
    {
        var clock = new BusinessClock(new FakeTimeProvider(Utc0300), () => "Asia/Seoul");

        var shown = clock.ToCompanyTime(Utc0300);

        shown.Should().Be(Utc0300);
        (shown.Hour, shown.Offset).Should().Be((12, TimeSpan.FromHours(9)));
    }

    [Fact, Trait("Id", "CORE-024-UT-04"), Trait("Rule", "BC-BR-318")]
    public void TimeZone_IsReadOnce_AndAgainAfterReset()
    {
        var reads = 0;
        var zone = "Asia/Seoul";
        var clock = new BusinessClock(new FakeTimeProvider(Utc0300), () => { reads++; return zone; });

        clock.Today().Should().Be(new DateOnly(2026, 1, 1));
        clock.Today();
        reads.Should().Be(1);

        zone = "America/New_York";
        clock.Today().Should().Be(new DateOnly(2026, 1, 1), "the cached zone stays until the company is saved");
        clock.Reset();

        clock.Today().Should().Be(new DateOnly(2025, 12, 31));
        reads.Should().Be(2);
    }

    [Fact, Trait("Id", "CORE-024-UT-05"), Trait("Rule", "BC-BR-318")]
    public void NoCompany_UsesTheUsDefault()
    {
        new BusinessClock(new FakeTimeProvider(Utc0300), () => null).TimeZone.Id.Should().Be("America/New_York");
    }
}
