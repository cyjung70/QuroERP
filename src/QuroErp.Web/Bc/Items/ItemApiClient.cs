using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using QuroErp.Contracts.Bc.Items;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Bc.Items;

/// <summary>Item image bytes with their MIME type.</summary>
public sealed record ItemImageContent(byte[] Content, string ContentType);

/// <summary>Item master API client (api/bc/items, BC-UC-009).</summary>
public sealed class ItemApiClient(HttpClient http, AccessTokenProvider tokens)
{
    private const int LookupPageSize = 100;

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

    /// <summary>Items for the find item dialog: the keyword matches the item code prefix or the item name.</summary>
    /// <returns>In item class, item code order.</returns>
    public async Task<IReadOnlyList<ItemDto>> LookupAsync(string? keyword, CancellationToken cancellationToken)
    {
        var byCode = await SearchAsync(new ItemSearchQuery { ItemCd = keyword, Size = LookupPageSize }, cancellationToken);
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return byCode.Items;
        }
        var byName = await SearchAsync(new ItemSearchQuery { ItemName = keyword, Size = LookupPageSize }, cancellationToken);
        return byCode.Items.Concat(byName.Items).DistinctBy(i => i.Id)
            .OrderBy(i => i.ItemClass, StringComparer.Ordinal).ThenBy(i => i.ItemCd, StringComparer.Ordinal).ToList();
    }

    /// <exception cref="ApiProblemException">404 Common-E-006 when the item is gone.</exception>
    public async Task<ItemDto> GetAsync(long id, CancellationToken cancellationToken)
    {
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, ItemUri(id)), cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <summary>The item before (forward = false) or after this one in item class, item code order; null at either end.</summary>
    public async Task<ItemDto?> GetAdjacentAsync(long id, bool forward, CancellationToken cancellationToken)
    {
        var uri = $"{ItemUri(id)}/{(forward ? ItemRoutes.Next : ItemRoutes.Previous)}";
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        return response.StatusCode == HttpStatusCode.NoContent ? null : await ReadAsync(response, cancellationToken);
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

    /// <summary>The item image, else its item group's (MM-BR-820).</summary>
    /// <returns>null when neither has an image or the item does not exist (404).</returns>
    public async Task<ItemImageContent?> GetImageAsync(string itemCd, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.SendAuthorizedAsync(
                tokens, new HttpRequestMessage(HttpMethod.Get, ItemRoutes.ImageOf(itemCd)), cancellationToken);
            var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            return new ItemImageContent(content, response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream");
        }
        catch (ApiProblemException e) when (e.Problem.Status == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>Replaces the whole item image with this file (MM-BR-822).</summary>
    /// <exception cref="ApiProblemException">400 MM-E-821, 403 for users without Full Access.</exception>
    public async Task UploadImageAsync(string itemCd, byte[] image, string fileName, CancellationToken cancellationToken)
    {
        var file = new ByteArrayContent(image);
        // The API decides the type from the content; the declared type is informational only
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var message = new HttpRequestMessage(HttpMethod.Put, ItemRoutes.ImageOf(itemCd))
        {
            Content = new MultipartFormDataContent { { file, ItemRoutes.ImageFormField, fileName } },
        };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
    }

    /// <summary>Clears the item image (MM-BR-823).</summary>
    /// <exception cref="ApiProblemException">404 Common-E-006 when the item is gone, 403 for users without Full Access.</exception>
    public async Task DeleteImageAsync(string itemCd, CancellationToken cancellationToken)
    {
        using var response = await http.SendAuthorizedAsync(
            tokens, new HttpRequestMessage(HttpMethod.Delete, ItemRoutes.ImageOf(itemCd)), cancellationToken);
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
