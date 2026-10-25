using FluentAssertions;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Domain.Tests.Common.Auth;

public class ApiClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);

    [Fact, Trait("Id", "CORE-003-T08")]
    public void IsActive_NotRevoked_ReturnsTrue()
    {
        new ApiClient().IsActive(Now).Should().BeTrue();
    }

    [Fact, Trait("Id", "CORE-003-T09")]
    public void IsActive_RevokedInPast_ReturnsFalse()
    {
        new ApiClient { RevokedAt = Now.AddSeconds(-1) }.IsActive(Now).Should().BeFalse();
    }

    [Fact, Trait("Id", "CORE-003-T10")]
    public void IsActive_RevokeScheduledInFuture_ReturnsTrue()
    {
        new ApiClient { RevokedAt = Now.AddDays(1) }.IsActive(Now).Should().BeTrue();
    }
}
