using QuroErp.Contracts.Mm.Inventory;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Mm.Inventory;

/// <summary>Inventory setting API client (GET, PUT api/mm/inventory-setting).</summary>
public sealed class InventorySettingApiClient(HttpClient http, AccessTokenProvider tokens)
{
    public async Task<InventorySettingDto> GetAsync(CancellationToken cancellationToken)
    {
        using var response = await http.SendAuthorizedAsync(
            tokens, new HttpRequestMessage(HttpMethod.Get, InventorySettingRoutes.Base), cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <exception cref="ApiProblemException">409 Common-E-004 when someone saved first, 403 for non-admin users.</exception>
    public async Task<InventorySettingDto> UpdateAsync(UpdateInventorySettingRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, InventorySettingRoutes.Base) { Content = JsonContent.Create(request) };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    private static async Task<InventorySettingDto> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<InventorySettingDto>(cancellationToken)
            ?? throw new InvalidOperationException("Inventory setting API returned an empty body.");
}
