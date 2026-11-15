using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Contracts.Sd.Assemblies;

namespace QuroErp.Api.Tests.Sd.Assemblies;

/// <summary>
/// /api/sd/assemblies access (SD-UC-055, BC-BR-214). The rules and the database behaviour are covered by the Domain tests and the
/// AssemblyServiceTests on real PostgreSQL; these check that the screen guards every change before anything is read.
/// </summary>
public class AssemblyApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact, Trait("Id", "BC-UT-214")]
    public async Task Changes_WithoutTheScreen_Return403()
    {
        var client = factory.NonAdmin();

        var responses = new[]
        {
            await client.PostAsJsonAsync(AssemblyRoutes.Base, new SaveAssemblyRequest(1, 1m, null, 1, null)),
            await client.PostAsJsonAsync("api/sd/assemblies/1/disassemble", new DisassembleRequest(1m, null, null)),
            await client.PostAsJsonAsync("api/sd/assemblies/1/void", new VoidAssemblyRequest(DateTimeOffset.UtcNow)),
        };

        responses.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reads_WithoutSignIn_Return401()
    {
        var client = factory.CreateClient();

        var responses = new[]
        {
            await client.GetAsync(AssemblyRoutes.Base),
            await client.GetAsync("api/sd/assemblies/1"),
            await client.GetAsync($"{AssemblyRoutes.Preview}?setItemId=1&qty=1&warehouseId=1"),
        };

        responses.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.Unauthorized);
    }
}
