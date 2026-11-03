using System.Globalization;
using System.Net;
using QuroErp.Contracts.Mm.Vendors;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Mm.Vendors;

/// <summary>Vendor master API client (api/mm/vendors, MM-UC-001).</summary>
public sealed class VendorApiClient(HttpClient http, AccessTokenProvider tokens)
{
    private const int LookupPageSize = 100;

    /// <summary>Vendors whose name contains the keyword, in name order.</summary>
    public async Task<VendorListDto> SearchAsync(VendorSearchQuery query, CancellationToken cancellationToken)
    {
        var parameters = new List<string>
        {
            $"page={query.Page.ToString(CultureInfo.InvariantCulture)}",
            $"size={query.Size.ToString(CultureInfo.InvariantCulture)}",
        };
        if (!string.IsNullOrWhiteSpace(query.VName))
        {
            parameters.Add($"vName={Uri.EscapeDataString(query.VName.Trim())}");
        }
        if (!string.IsNullOrWhiteSpace(query.ValidFlag))
        {
            parameters.Add($"validFlag={Uri.EscapeDataString(query.ValidFlag.Trim())}");
        }
        using var response = await http.SendAuthorizedAsync(
            tokens, new HttpRequestMessage(HttpMethod.Get, $"{VendorRoutes.Base}?{string.Join('&', parameters)}"), cancellationToken);
        return await response.Content.ReadFromJsonAsync<VendorListDto>(cancellationToken)
            ?? throw new InvalidOperationException("Vendor API returned an empty body.");
    }

    /// <summary>The vendor with exactly this name (names are stored in upper case, MM-BR-852), or null.</summary>
    /// <remarks>The list matches names that contain the text, so the exact name is picked from the matches.</remarks>
    public async Task<VendorDto?> FindByNameAsync(string vName, CancellationToken cancellationToken)
    {
        var list = await SearchAsync(new VendorSearchQuery { VName = vName, Size = VendorRoutes.MaxPageSize }, cancellationToken);
        return list.Items.FirstOrDefault(v => string.Equals(v.VName, vName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Vendors for the find vendor dialog, in name order.</summary>
    public async Task<IReadOnlyList<VendorDto>> LookupAsync(string? keyword, CancellationToken cancellationToken) =>
        (await SearchAsync(new VendorSearchQuery { VName = keyword, Size = LookupPageSize }, cancellationToken)).Items;

    /// <exception cref="ApiProblemException">404 Common-E-006 when the vendor is gone.</exception>
    public async Task<VendorDto> GetAsync(long id, CancellationToken cancellationToken)
    {
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, VendorUri(id)), cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <summary>The vendor before (forward = false) or after this one in name order; null at either end.</summary>
    public async Task<VendorDto?> GetAdjacentAsync(long id, bool forward, CancellationToken cancellationToken)
    {
        var uri = $"{VendorUri(id)}/{(forward ? VendorRoutes.Next : VendorRoutes.Previous)}";
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        return response.StatusCode == HttpStatusCode.NoContent ? null : await ReadAsync(response, cancellationToken);
    }

    /// <exception cref="ApiProblemException">422 MM-E-851~872, BC-E-415, BC-E-521~523 or Common-E-002; 403 without Full Access.</exception>
    public async Task<VendorDto> CreateAsync(CreateVendorRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, VendorRoutes.Base) { Content = JsonContent.Create(request) };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <exception cref="ApiProblemException">422 as for create, 409 Common-E-004 when someone saved first.</exception>
    public async Task<VendorDto> UpdateAsync(long id, UpdateVendorRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, VendorUri(id)) { Content = JsonContent.Create(request) };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <exception cref="ApiProblemException">422 MM-E-857 when the vendor is used, 409 Common-E-004.</exception>
    public async Task DeleteAsync(long id, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        var uri = $"{VendorUri(id)}?updatedAt={Uri.EscapeDataString(updatedAt.ToString("O", CultureInfo.InvariantCulture))}";
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Delete, uri), cancellationToken);
    }

    private static string VendorUri(long id) => $"{VendorRoutes.Base}/{id.ToString(CultureInfo.InvariantCulture)}";

    private static async Task<VendorDto> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<VendorDto>(cancellationToken)
            ?? throw new InvalidOperationException("Vendor API returned an empty body.");
}
