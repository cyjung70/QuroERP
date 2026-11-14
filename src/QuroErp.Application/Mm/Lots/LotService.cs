using QuroErp.Application.Common;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Mm.Lots;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Inventory;

namespace QuroErp.Application.Mm.Lots;

/// <summary>Lot search and FIFO stock suggestions of the stock document screens (MM-BR-362, MM-BR-716).</summary>
public sealed class LotService(ILotRepository lots, IUnitOfWorkFactory unitOfWork, IBusinessClock clock)
{
    /// <summary>A required condition is missing. Arg: its name.</summary>
    public const string RequiredCode = "Common-E-005";

    /// <summary>
    /// The (Location, Lot) balances of the item in the warehouse above zero at the end of the date, in FIFO order: Lot '*' first,
    /// then received date, Lot number and Location code.
    /// </summary>
    /// <exception cref="DomainException">Common-E-005 when itemId, warehouseId or date is missing.</exception>
    public async Task<IReadOnlyList<LotBalanceDto>> SearchAsync(LotSearchQuery query, CancellationToken cancellationToken)
    {
        var itemId = query.ItemId ?? throw new DomainException(RequiredCode, "itemId");
        var warehouseId = query.WarehouseId ?? throw new DomainException(RequiredCode, "warehouseId");
        var date = query.Date ?? throw new DomainException(RequiredCode, "date");

        var balances = await FindBalancesAsync(itemId, warehouseId, date, cancellationToken);
        return StockSuggestion.FifoOrder(balances)
            .Select(b => new LotBalanceDto(b.LotId, b.LotNo, b.Qty, b.LocationId, b.LocationCode)).ToList();
    }

    /// <summary>
    /// Where a decrease of qty should take its stock: the balances at the end of the date filled in FIFO order, split over
    /// several (Location, Lot) when one is not enough. Short stock gives parts that add up to less than qty, and ShortQty is the
    /// rest (MM-BR-716).
    /// </summary>
    /// <exception cref="DomainException">Common-E-005 when itemId, warehouseId or a qty above zero is missing.</exception>
    public async Task<StockSuggestionResultDto> SuggestAsync(StockSuggestionQuery query, CancellationToken cancellationToken)
    {
        var itemId = query.ItemId ?? throw new DomainException(RequiredCode, "itemId");
        var warehouseId = query.WarehouseId ?? throw new DomainException(RequiredCode, "warehouseId");
        var qty = query.Qty is { } q && q > 0 ? q : throw new DomainException(RequiredCode, "qty");

        var balances = await FindBalancesAsync(itemId, warehouseId, query.Date ?? clock.Today(), cancellationToken);
        var picks = StockSuggestion.Fifo(balances, qty);
        return new StockSuggestionResultDto(
            [.. picks.Select(p => new StockSuggestionDto(p.LocationId, p.LocationCode, p.LotId, p.LotNo, p.Qty))],
            StockSuggestion.ShortQty(picks, qty));
    }

    private async Task<IReadOnlyList<StockBalance>> FindBalancesAsync(long itemId, long warehouseId, DateOnly date, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var rows = await lots.FindBalancesAsync(itemId, warehouseId, date, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return rows;
    }
}
