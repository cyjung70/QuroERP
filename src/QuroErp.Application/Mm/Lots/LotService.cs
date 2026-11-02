using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Mm.Lots;
using QuroErp.Domain.Common;

namespace QuroErp.Application.Mm.Lots;

/// <summary>Lot search of the stock document screens (MM-BR-362).</summary>
public sealed class LotService(ILotRepository lots, IUnitOfWorkFactory unitOfWork)
{
    /// <summary>A required condition is missing. Arg: its name.</summary>
    public const string RequiredCode = "Common-E-005";

    /// <summary>Lots of the item in the warehouse with a balance above zero at the end of the date, in lot number order.</summary>
    /// <exception cref="DomainException">Common-E-005 when itemId, warehouseId or date is missing.</exception>
    public async Task<IReadOnlyList<LotBalanceDto>> SearchAsync(LotSearchQuery query, CancellationToken cancellationToken)
    {
        var itemId = query.ItemId ?? throw new DomainException(RequiredCode, "itemId");
        var warehouseId = query.WarehouseId ?? throw new DomainException(RequiredCode, "warehouseId");
        var date = query.Date ?? throw new DomainException(RequiredCode, "date");

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var rows = await lots.FindBalancesAsync(itemId, warehouseId, date, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return rows.Select(r => new LotBalanceDto(r.LotId, r.LotNo, r.OnHandQty)).ToList();
    }
}
