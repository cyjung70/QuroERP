using System.Globalization;
using QuroErp.Contracts.Sd.Assemblies;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Sd.Assemblies;

/// <summary>Set assembly and disassembly API client (api/sd/assemblies, SD-UC-055).</summary>
public sealed class AssemblyApiClient(HttpClient http, AccessTokenProvider tokens)
{
    /// <summary>Assemblies and disassemblies that match the conditions, newest first (at most <see cref="AssemblyRoutes.MaxListSize"/>).</summary>
    public async Task<IReadOnlyList<AssemblySummaryDto>> SearchAsync(AssemblySearchQuery query, CancellationToken cancellationToken)
    {
        var parameters = new List<string>();
        Add(parameters, "from", Date(query.From));
        Add(parameters, "to", Date(query.To));
        Add(parameters, "setItemId", query.SetItemId?.ToString(CultureInfo.InvariantCulture));
        Add(parameters, "kind", query.Kind);
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, Uri(AssemblyRoutes.Base, parameters)), cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<AssemblySummaryDto>>(cancellationToken) ?? [];
    }

    /// <exception cref="ApiProblemException">404 Common-E-006 when the assembly is gone.</exception>
    public async Task<AssemblyDto> GetAsync(long id, CancellationToken cancellationToken)
    {
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, ById(id)), cancellationToken);
        return await response.Content.ReadFromJsonAsync<AssemblyDto>(cancellationToken)
            ?? throw new InvalidOperationException("Assembly API returned an empty body.");
    }

    /// <summary>The components of qty Sets and their FIFO Locations and Lots (SD-BR-518); nothing is saved.</summary>
    /// <exception cref="ApiProblemException">422 Common-E-005/002, SD-E-509.</exception>
    public async Task<AssemblyPreviewDto> PreviewAsync(AssemblyPreviewQuery query, CancellationToken cancellationToken)
    {
        var parameters = new List<string>();
        Add(parameters, "setItemId", query.SetItemId?.ToString(CultureInfo.InvariantCulture));
        Add(parameters, "qty", query.Qty?.ToString(CultureInfo.InvariantCulture));
        Add(parameters, "warehouseId", query.WarehouseId?.ToString(CultureInfo.InvariantCulture));
        Add(parameters, "assemblyDt", Date(query.AssemblyDt));
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, Uri(AssemblyRoutes.Preview, parameters)), cancellationToken);
        return await response.Content.ReadFromJsonAsync<AssemblyPreviewDto>(cancellationToken)
            ?? throw new InvalidOperationException("Assembly API returned an empty body.");
    }

    /// <exception cref="ApiProblemException">422 SD-E-514, SD-E-509, Common-E-005/002 and the inventory checks.</exception>
    public async Task<AssemblyCreatedDto> AssembleAsync(SaveAssemblyRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, AssemblyRoutes.Base) { Content = JsonContent.Create(request) };
        return await CreatedAsync(message, cancellationToken);
    }

    /// <exception cref="ApiProblemException">404 Common-E-006; 422 SD-E-516 and the inventory checks.</exception>
    public async Task<AssemblyCreatedDto> DisassembleAsync(long id, DisassembleRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{ById(id)}/disassemble") { Content = JsonContent.Create(request) };
        return await CreatedAsync(message, cancellationToken);
    }

    /// <summary>Voids the assembly or disassembly (MM-BR-601).</summary>
    /// <exception cref="ApiProblemException">409 Common-E-004 when stale, SD-E-516 when the assembly has a disassembly.</exception>
    public async Task<AssemblySummaryDto> VoidAsync(long id, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{ById(id)}/void") { Content = JsonContent.Create(new VoidAssemblyRequest(updatedAt)) };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await response.Content.ReadFromJsonAsync<AssemblySummaryDto>(cancellationToken)
            ?? throw new InvalidOperationException("Assembly API returned an empty body.");
    }

    private async Task<AssemblyCreatedDto> CreatedAsync(HttpRequestMessage message, CancellationToken cancellationToken)
    {
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await response.Content.ReadFromJsonAsync<AssemblyCreatedDto>(cancellationToken)
            ?? throw new InvalidOperationException("Assembly API returned an empty body.");
    }

    private static string? Date(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static void Add(List<string> parameters, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parameters.Add($"{name}={System.Uri.EscapeDataString(value)}");
        }
    }

    private static string Uri(string path, List<string> parameters) =>
        parameters.Count == 0 ? path : $"{path}?{string.Join('&', parameters)}";

    private static string ById(long id) => $"{AssemblyRoutes.Base}/{id.ToString(CultureInfo.InvariantCulture)}";
}
