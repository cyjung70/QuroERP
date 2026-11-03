using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using QuroErp.Contracts.Common.Auth;

namespace QuroErp.Api.Tests;

/// <summary>Clients for each kind of caller: admin user (JWT), non-admin API client (X-Api-Key, role manager).</summary>
public static class ApiClients
{
    // Login writes a login log each time, so the tests share one token. Every factory uses the same signing key
    private static string? _adminToken;

    public static async Task<HttpClient> AdminAsync(this ApiFactory factory)
    {
        var client = factory.CreateClient();
        if (_adminToken is null)
        {
            using var response = await client.PostAsJsonAsync(AuthRoutes.Login, new LoginRequest(ApiFactory.UserId, ApiFactory.Password));
            response.EnsureSuccessStatusCode();
            _adminToken = (await response.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
        return client;
    }

    public static HttpClient NonAdmin(this ApiFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(AuthRoutes.ApiKeyHeader, ApiFactory.ApiKey);
        return client;
    }

    public static async Task<JsonElement> ReadProblemAsync(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private static readonly string[] AuditFields = ["createdAt", "createdBy", "updatedAt", "updatedBy"];

    /// <summary>The audit field names an S screen reads from a one-record response (api.md, TASK-CORE-050).</summary>
    public static async Task<IEnumerable<string>> ReadAuditFieldsAsync(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return AuditFields.Where(name => body.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() != "")
            .ToList();
    }
}
