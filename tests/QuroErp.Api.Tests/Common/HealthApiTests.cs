using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace QuroErp.Api.Tests.Common;

/// <summary>GET /health for the container healthcheck (reports.md "배포"): no sign-in, 503 when a check fails.</summary>
public class HealthApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact, Trait("Id", "CORE-033-IT-04")]
    public async Task Health_WithoutSignIn_Is200WhenHealthy_And503WhenNot()
    {
        var client = factory.CreateClient();

        factory.Health = HealthStatus.Healthy;
        using var healthy = await client.GetAsync("/health");
        factory.Health = HealthStatus.Unhealthy;
        using var unhealthy = await client.GetAsync("/health");

        (healthy.StatusCode, await healthy.Content.ReadAsStringAsync()).Should().Be((HttpStatusCode.OK, "Healthy"));
        unhealthy.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}
