using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Contracts.Bc.Companies;
using QuroErp.Contracts.Bc.Items;
using QuroErp.Contracts.Common;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Contracts.Mm.Warehouses;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Api.Tests.Common.Auth;

/// <summary>
/// 403 without an account-state code carries Common-E-007 (TASK-CORE-081, api.md "오류", BC-BR-214); AUTH-005 keeps its code.
/// User "profile" (role user) reads the item menu only and has no company menu; the API key's role has no menus.
/// </summary>
public class ForbiddenProblemTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact, Trait("Rule", "BC-BR-214"), Trait("Id", "BC-TS-003")]
    public async Task ChangeOnAReadOnlyMenu_Is403CommonE007()
    {
        var client = await SignInAsync(ApiFactory.ProfileUserId);

        using var image = await client.DeleteAsync($"{ItemRoutes.Base}/BOLT/{ItemRoutes.Image}");
        using var save = await client.PutAsJsonAsync($"{ItemRoutes.Base}/1", new { });

        await ShouldBeForbiddenAsync(image);
        await ShouldBeForbiddenAsync(save);
    }

    [Fact, Trait("Rule", "BC-BR-214"), Trait("Id", "BC-TS-003")]
    public async Task RequestOnAMenuWithoutAssignment_Is403CommonE007()
    {
        var client = await SignInAsync(ApiFactory.ProfileUserId);

        using var response = await client.DeleteAsync(CompanyRoutes.Logo);

        await ShouldBeForbiddenAsync(response);
    }

    [Fact, Trait("Rule", "BC-BR-214")]
    public async Task ApiKeyCallerWithoutTheMenu_Is403CommonE007()
    {
        using var response = await factory.NonAdmin().PostAsJsonAsync(
            WarehouseRoutes.Batch, new BatchRequest<WarehouseSaveRow, WarehouseKey>([], [], []));

        await ShouldBeForbiddenAsync(response);
    }

    [Fact]
    public async Task MustChangePassword_Keeps403Auth005()
    {
        var client = await SignInAsync(ApiFactory.NewUserId);

        using var response = await client.DeleteAsync(CompanyRoutes.Logo);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be(AuthCodes.PasswordChangeRequired);
    }

    private static async Task ShouldBeForbiddenAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be(ProblemCodes.Forbidden);
        problem.GetProperty("args").GetArrayLength().Should().Be(0);
    }

    private async Task<HttpClient> SignInAsync(string userId)
    {
        var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(AuthRoutes.Login, new LoginRequest(userId, ApiFactory.Password));
        response.EnsureSuccessStatusCode();
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }
}
