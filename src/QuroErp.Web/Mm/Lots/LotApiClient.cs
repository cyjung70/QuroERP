using System.Globalization;
using QuroErp.Contracts.Mm.Lots;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Mm.Lots;

/// <summary>Lot search of a stock document line (api/mm/lots, MM-BR-362).</summary>
public sealed class LotApiClient(HttpClient http, AccessTokenProvider tokens)
{
    /// <summary>Lots of the item in the warehouse with a balance above zero at the end of the date, in lot number order.</summary>
    public async Task<IReadOnlyList<LotBalanceDto>> SearchAsync(long itemId, long warehouseId, DateOnly date, CancellationToken cancellationToken)
    {
        var uri = string.Create(CultureInfo.InvariantCulture,
            $"{LotRoutes.Base}?itemId={itemId}&warehouseId={warehouseId}&date={date:yyyy-MM-dd}");
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<LotBalanceDto>>(cancellationToken) ?? [];
    }
}
