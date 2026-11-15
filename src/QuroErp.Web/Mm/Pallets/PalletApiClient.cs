using System.Globalization;
using QuroErp.Contracts.Mm.Pallets;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Mm.Pallets;

/// <summary>Pallet API client (api/mm/pallets: search, one Pallet with its history, split and merge, MM-UC-035).</summary>
public sealed class PalletApiClient(HttpClient http, AccessTokenProvider tokens)
{
    /// <summary>Pallets that match every given condition, in number order.</summary>
    public async Task<IReadOnlyList<PalletDto>> SearchAsync(PalletSearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var conditions = new List<string>();
        Add(conditions, "palletNo", query.PalletNo);
        Add(conditions, "itemId", query.ItemId?.ToString(CultureInfo.InvariantCulture));
        Add(conditions, "lotNo", query.LotNo);
        Add(conditions, "warehouseId", query.WarehouseId?.ToString(CultureInfo.InvariantCulture));
        Add(conditions, "locationId", query.LocationId?.ToString(CultureInfo.InvariantCulture));
        Add(conditions, "status", query.Status);
        var uri = conditions.Count == 0 ? PalletRoutes.Base : $"{PalletRoutes.Base}?{string.Join('&', conditions)}";
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<PalletDto>>(cancellationToken)
            ?? throw new InvalidOperationException("Pallet API returned an empty body.");
    }

    /// <summary>The Pallet with its history (MM-BR-916).</summary>
    /// <exception cref="ApiProblemException">404 Common-E-006.</exception>
    public async Task<PalletDto> GetAsync(long id, CancellationToken cancellationToken)
    {
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, Path(id)), cancellationToken);
        return await response.Content.ReadFromJsonAsync<PalletDto>(cancellationToken)
            ?? throw new InvalidOperationException("Pallet API returned an empty body.");
    }

    /// <summary>Splits qty off the Pallet into a new one (MM-BR-914).</summary>
    /// <exception cref="ApiProblemException">422 MM-E-914; 409 Common-E-004; 404 Common-E-006.</exception>
    public async Task<SplitPalletResultDto> SplitAsync(long id, SplitPalletRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, Path(id) + "/split") { Content = JsonContent.Create(request) };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await response.Content.ReadFromJsonAsync<SplitPalletResultDto>(cancellationToken)
            ?? throw new InvalidOperationException("Pallet API returned an empty body.");
    }

    /// <summary>Merges the source Pallets into the target and returns the target (MM-BR-915).</summary>
    /// <exception cref="ApiProblemException">422 MM-E-915; 404 Common-E-006.</exception>
    public async Task<PalletDto> MergeAsync(MergePalletsRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, PalletRoutes.Merge) { Content = JsonContent.Create(request) };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await response.Content.ReadFromJsonAsync<PalletDto>(cancellationToken)
            ?? throw new InvalidOperationException("Pallet API returned an empty body.");
    }

    private static string Path(long id) => $"{PalletRoutes.Base}/{id.ToString(CultureInfo.InvariantCulture)}";

    private static void Add(List<string> conditions, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            conditions.Add($"{name}={Uri.EscapeDataString(value.Trim())}");
        }
    }
}
