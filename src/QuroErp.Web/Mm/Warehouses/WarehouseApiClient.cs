using QuroErp.Contracts.Common;
using QuroErp.Contracts.Mm.Warehouses;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Mm.Warehouses;

/// <summary>Warehouse API client (api/mm/warehouses: the master, MM-UC-017, and the choice list).</summary>
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

    /// <summary>Every warehouse in code order; warehouses not in use only with includeInactive.</summary>
    public async Task<IReadOnlyList<WarehouseDto>> SearchAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        var uri = includeInactive ? $"{WarehouseRoutes.Base}?includeInactive=true" : WarehouseRoutes.Base;
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <summary>Saves the changed rows together and returns every warehouse.</summary>
    /// <exception cref="ApiProblemException">422 MM-E-701, MM-E-702, MM-E-704 (code, reason) or MM-E-709; 409 Common-E-004.</exception>
    public async Task<IReadOnlyList<WarehouseDto>> SaveAsync(
        BatchRequest<WarehouseSaveRow, WarehouseKey> request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, WarehouseRoutes.Batch) { Content = JsonContent.Create(request) };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    private static async Task<IReadOnlyList<WarehouseDto>> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<List<WarehouseDto>>(cancellationToken)
        ?? throw new InvalidOperationException("Warehouse API returned an empty body.");
}
