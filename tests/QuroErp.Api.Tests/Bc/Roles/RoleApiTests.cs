using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Contracts.Bc.Roles;

namespace QuroErp.Api.Tests.Bc.Roles;

/// <summary>GET /api/sys/roles: roles in use for role select fields (role-menu spec, used by BC-UC-004).</summary>
public class RoleApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact, Trait("Id", "BC-UT-122")]
    public async Task Roles_ForAnySignedInCaller_ReturnIdAndCode()
    {
        var roles = await factory.NonAdmin().GetFromJsonAsync<List<RoleDto>>(RoleRoutes.Base);

        roles.Should().Equal(new RoleDto(1, "admin"), new RoleDto(2, "user"));
    }

    [Fact, Trait("Id", "BC-UT-132")]
    public async Task Roles_WithoutSignIn_Return401()
    {
        using var response = await factory.CreateClient().GetAsync(RoleRoutes.Base);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
