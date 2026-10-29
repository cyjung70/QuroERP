using QuroErp.Contracts.Mm.Warehouses;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Mm.Warehouses;

/// <summary>Warehouse API client (GET api/mm/warehouses/options).</summary>
public sealed class WarehouseApiClient(HttpClient http, AccessTokenProvider tokens)
{
    /// <summary>Warehouses in use, in warehouse code order, for select fields.</summary>
    public async Task<IReadOnlyList<WarehouseOptionDto>> GetOptionsAsync(CancellationToken cancellationToken)
    {
        using var response = await http.SendAuthorizedAsync(
            tokens, new HttpRequestMessage(HttpMethod.Get, WarehouseRoutes.Options), cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<WarehouseOptionDto>>(cancellationToken)
            ?? throw new InvalidOperationException("Warehouse API returned an empty body.");
    }
}
