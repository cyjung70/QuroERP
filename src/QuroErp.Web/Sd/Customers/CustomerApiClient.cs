using System.Globalization;
using System.Net;
using QuroErp.Contracts.Common;
using QuroErp.Contracts.Sd.Customers;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Sd.Customers;

/// <summary>Customer master API client (api/sd/customers, SD-UC-001).</summary>
public sealed class CustomerApiClient(HttpClient http, AccessTokenProvider tokens)
{
    private const int LookupPageSize = 100;

    /// <summary>Customers whose name contains the keyword, in name order.</summary>
    public async Task<CustomerListDto> SearchAsync(CustomerSearchQuery query, CancellationToken cancellationToken)
    {
        var parameters = new List<string>
        {
            $"page={query.Page.ToString(CultureInfo.InvariantCulture)}",
            $"size={query.Size.ToString(CultureInfo.InvariantCulture)}",
        };
        if (!string.IsNullOrWhiteSpace(query.CName))
        {
            parameters.Add($"cName={Uri.EscapeDataString(query.CName.Trim())}");
        }
        if (!string.IsNullOrWhiteSpace(query.ValidFlag))
        {
            parameters.Add($"validFlag={Uri.EscapeDataString(query.ValidFlag.Trim())}");
        }
        using var response = await http.SendAuthorizedAsync(
            tokens, new HttpRequestMessage(HttpMethod.Get, $"{CustomerRoutes.Base}?{string.Join('&', parameters)}"), cancellationToken);
        return await response.Content.ReadFromJsonAsync<CustomerListDto>(cancellationToken)
            ?? throw new InvalidOperationException("Customer API returned an empty body.");
    }

    /// <summary>The customer with exactly this name (names are stored in upper case, SD-BR-302), or null.</summary>
    /// <remarks>The list matches names that contain the text, so the exact name is picked from the matches.</remarks>
    public async Task<CustomerDto?> FindByNameAsync(string cName, CancellationToken cancellationToken)
    {
        var list = await SearchAsync(new CustomerSearchQuery { CName = cName, Size = CustomerRoutes.MaxPageSize }, cancellationToken);
        return list.Items.FirstOrDefault(c => string.Equals(c.CName, cName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Customers for the find customer dialog, in name order.</summary>
    public async Task<IReadOnlyList<CustomerDto>> LookupAsync(string? keyword, CancellationToken cancellationToken) =>
        (await SearchAsync(new CustomerSearchQuery { CName = keyword, Size = LookupPageSize }, cancellationToken)).Items;

    /// <exception cref="ApiProblemException">404 Common-E-006 when the customer is gone.</exception>
    public async Task<CustomerDto> GetAsync(long id, CancellationToken cancellationToken)
    {
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, CustomerUri(id)), cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <exception cref="ApiProblemException">422 SD-E-301~335, BC-E-415, BC-E-522, BC-E-523 or Common-E-002; 403 without Full Access.</exception>
    public async Task<CustomerDto> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, CustomerRoutes.Base) { Content = JsonContent.Create(request) };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <exception cref="ApiProblemException">422 as for create, 409 Common-E-004 when someone saved first.</exception>
    public async Task<CustomerDto> UpdateAsync(long id, UpdateCustomerRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, CustomerUri(id)) { Content = JsonContent.Create(request) };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <exception cref="ApiProblemException">422 SD-E-307 when the customer has documents, 409 Common-E-004.</exception>
    public async Task DeleteAsync(long id, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        var uri = $"{CustomerUri(id)}?updatedAt={Uri.EscapeDataString(updatedAt.ToString("O", CultureInfo.InvariantCulture))}";
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Delete, uri), cancellationToken);
    }

    /// <summary>The customer's exemption certificates (BC-BR-524), or null in a KR company, which has none (404).</summary>
    public async Task<IReadOnlyList<CustomerTaxExemptionDto>?> GetTaxExemptionsAsync(long id, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.SendAuthorizedAsync(
                tokens, new HttpRequestMessage(HttpMethod.Get, ExemptionsUri(id)), cancellationToken);
            return await ReadExemptionsAsync(response, cancellationToken);
        }
        catch (ApiProblemException e) when (e.Problem.Status == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>Saves the added, changed and deleted certificates together and returns all of them.</summary>
    /// <exception cref="ApiProblemException">422 BC-E-524 or SD-E-333, 409 Common-E-004.</exception>
    public async Task<IReadOnlyList<CustomerTaxExemptionDto>> SaveTaxExemptionsAsync(
        long id, BatchRequest<CustomerTaxExemptionSaveRow, CustomerTaxExemptionKey> request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, ExemptionsUri(id)) { Content = JsonContent.Create(request) };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await ReadExemptionsAsync(response, cancellationToken);
    }

    private static string CustomerUri(long id) => $"{CustomerRoutes.Base}/{id.ToString(CultureInfo.InvariantCulture)}";

    private static string ExemptionsUri(long id) => $"{CustomerUri(id)}/{CustomerRoutes.TaxExemptions}";

    private static async Task<CustomerDto> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<CustomerDto>(cancellationToken)
            ?? throw new InvalidOperationException("Customer API returned an empty body.");

    private static async Task<IReadOnlyList<CustomerTaxExemptionDto>> ReadExemptionsAsync(
        HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<List<CustomerTaxExemptionDto>>(cancellationToken)
            ?? throw new InvalidOperationException("Customer API returned an empty body.");
}
