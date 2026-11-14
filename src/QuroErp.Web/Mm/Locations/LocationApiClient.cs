using System.Globalization;
using QuroErp.Contracts.Common;
using QuroErp.Contracts.Mm.Locations;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Mm.Locations;

/// <summary>Location API client (api/mm/locations: the master per warehouse, MM-UC-034, and the choice list).</summary>
public sealed class LocationApiClient(HttpClient http, AccessTokenProvider tokens)
{
    /// <summary>Every Location of the warehouse, those not in use too, '*' first and then in code order.</summary>
    public async Task<IReadOnlyList<LocationDto>> SearchAsync(long warehouseId, CancellationToken cancellationToken)
    {
        var uri = $"{LocationRoutes.Base}?warehouseId={warehouseId.ToString(CultureInfo.InvariantCulture)}&includeInactive=true";
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <summary>Locations in use of the warehouse, '*' first, for new document lines (MM-BR-717).</summary>
    public async Task<IReadOnlyList<LocationOptionDto>> GetOptionsAsync(long warehouseId, CancellationToken cancellationToken)
    {
        var uri = $"{LocationRoutes.Options}?warehouseId={warehouseId.ToString(CultureInfo.InvariantCulture)}";
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<LocationOptionDto>>(cancellationToken)
            ?? throw new InvalidOperationException("Location API returned an empty body.");
    }

    /// <summary>Saves the changed rows together and returns the Locations of the warehouses in the save.</summary>
    /// <exception cref="ApiProblemException">422 MM-E-712 (code), MM-E-717 (code, reason), Common-E-005; 409 Common-E-004.</exception>
    public async Task<IReadOnlyList<LocationDto>> SaveAsync(BatchRequest<LocationSaveRow, LocationKey> request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, LocationRoutes.Batch) { Content = JsonContent.Create(request) };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    private static async Task<IReadOnlyList<LocationDto>> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<List<LocationDto>>(cancellationToken)
        ?? throw new InvalidOperationException("Location API returned an empty body.");
}
