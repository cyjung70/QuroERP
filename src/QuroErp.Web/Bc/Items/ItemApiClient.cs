using System.Globalization;
using QuroErp.Contracts.Bc.Items;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Bc.Items;

/// <summary>Item master API client (api/bc/items, BC-UC-009).</summary>
public sealed class ItemApiClient(HttpClient http, AccessTokenProvider tokens)
{
    public async Task<ItemListDto> SearchAsync(ItemSearchQuery query, CancellationToken cancellationToken)
    {
        var parameters = new List<string>
        {
            $"page={query.Page.ToString(CultureInfo.InvariantCulture)}",
            $"size={query.Size.ToString(CultureInfo.InvariantCulture)}",
        };
        Add(parameters, "itemCd", query.ItemCd);
        Add(parameters, "itemName", query.ItemName);
        Add(parameters, "itemClass", query.ItemClass);
        Add(parameters, "validFlag", query.ValidFlag);

        using var response = await http.SendAuthorizedAsync(
            tokens, new HttpRequestMessage(HttpMethod.Get, $"{ItemRoutes.Base}?{string.Join('&', parameters)}"), cancellationToken);
        return await response.Content.ReadFromJsonAsync<ItemListDto>(cancellationToken)
            ?? throw new InvalidOperationException("Item API returned an empty body.");
    }

    /// <summary>The item with exactly this code, or null.</summary>
    /// <remarks>The list matches by prefix, so the exact code is picked from the first page of matches.</remarks>
    public async Task<ItemDto?> FindByCodeAsync(string itemCd, CancellationToken cancellationToken)
    {
        var list = await SearchAsync(new ItemSearchQuery { ItemCd = itemCd, Size = ItemRoutes.MaxPageSize }, cancellationToken);
        return list.Items.FirstOrDefault(i => string.Equals(i.ItemCd, itemCd, StringComparison.OrdinalIgnoreCase));
    }

    /// <exception cref="ApiProblemException">404 Common-E-006 when the item is gone.</exception>
    public async Task<ItemDto> GetAsync(long id, CancellationToken cancellationToken)
    {
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, ItemUri(id)), cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <exception cref="ApiProblemException">422 MM-E-801~831 or Common-E-002, 403 for users without Full Access.</exception>
    public async Task<ItemDto> CreateAsync(CreateItemRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, ItemRoutes.Base) { Content = JsonContent.Create(request) };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <exception cref="ApiProblemException">422 MM-E-802~831, 409 Common-E-004 when someone saved first.</exception>
    public async Task<ItemDto> UpdateAsync(long id, UpdateItemRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, ItemUri(id)) { Content = JsonContent.Create(request) };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <exception cref="ApiProblemException">422 MM-E-807 when the item is used, 409 Common-E-004.</exception>
    public async Task DeleteAsync(long id, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        var uri = $"{ItemUri(id)}?updatedAt={Uri.EscapeDataString(updatedAt.ToString("O", CultureInfo.InvariantCulture))}";
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Delete, uri), cancellationToken);
    }

    private static string ItemUri(long id) => $"{ItemRoutes.Base}/{id.ToString(CultureInfo.InvariantCulture)}";

    private static void Add(List<string> parameters, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parameters.Add($"{name}={Uri.EscapeDataString(value)}");
        }
    }

    private static async Task<ItemDto> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<ItemDto>(cancellationToken)
            ?? throw new InvalidOperationException("Item API returned an empty body.");
}
