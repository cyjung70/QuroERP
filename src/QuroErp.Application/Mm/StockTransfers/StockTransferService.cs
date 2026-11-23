using System.Data;
using System.Globalization;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Common;
using QuroErp.Application.Common.DocumentNumbers;
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Mm.Inventory;
using QuroErp.Application.Mm.Pallets;
using QuroErp.Application.Mm.Warehouses;
using QuroErp.Contracts.Mm.StockTransfers;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Inventory;
using QuroErp.Domain.Mm.Labels;
using QuroErp.Domain.Mm.Warehouses;

namespace QuroErp.Application.Mm.StockTransfers;

/// <summary>
/// Stock transfers between warehouses (TR) and between Locations of one warehouse (LT) (MM-UC-018, MM-BR-705~708, MM-BR-715). Each
/// line issues its Lot from the sending side and receives it at the receiving side at the same price and amount, in the
/// document's transaction (MM-BR-706, MM-BR-357). A change or a delete reverses every movement of the transfer first, so the
/// receiving side is checked as a decrease (MM-BR-707); a change then posts its lines again.
/// </summary>
public sealed class StockTransferService(
    IStockTransferRepository transfers,
    IInventoryRepository stock,
    IPalletRepository pallets,
    IWarehouseRepository warehouseMasters,
    ILocalizationRepository localization,
    InventoryService inventory,
    DocumentNumberService numbers,
    IBusinessClock clock,
    IUnitOfWorkFactory unitOfWork)
{
    public const string RequiredCode = "Common-E-005";
    public const string InvalidCode = "Common-E-002";
    public const string NotFoundCode = "Common-E-006";

    /// <summary>Transfers, newest first, at most <see cref="StockTransferRoutes.MaxListSize"/> rows.</summary>
    /// <exception cref="DomainException">422 Common-E-002 for a movement type other than TR or LT.</exception>
    public async Task<IReadOnlyList<StockTransferSummaryDto>> SearchAsync(StockTransferSearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var movType = Upper(query.MovType);
        if (movType is not null && !StockTransferRules.IsType(movType))
        {
            throw new DomainException(InvalidCode, "movType", query.MovType!);
        }
        var filter = new StockTransferFilter(query.From, query.To, Upper(query.TransferNo), query.WarehouseId, movType, StockTransferRoutes.MaxListSize);
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var rows = await transfers.SearchAsync(filter, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return [.. rows.Select(r => r.ToSummary())];
    }

    /// <summary>
    /// The PDA move reads a scanned place (MM-BR-922, MM-BR-923): a Location label at FROM or TO, a Pallet label at FROM. Read only;
    /// the PDA saves through <see cref="CreateAsync"/>.
    /// </summary>
    /// <exception cref="DomainException">
    /// 422 Common-E-005 without a value, Common-E-002 for a step other than FROM or TO, MM-E-923 for a value that is not a label of
    /// this system, a kind the step does not take, or a Location, warehouse or Pallet not found, not in use or not ACTIVE.
    /// </exception>
    public async Task<StockTransferPlaceDto> ScanPlaceAsync(StockTransferScanQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var scanned = Trim(query.Value) ?? throw new DomainException(RequiredCode, "value");
        var kinds = PdaMoveRules.KindsOf(query.Step);
        var scan = LabelCode.Parse(scanned);
        LabelCode.EnsureAccepted(scan.Kind, [.. kinds], scanned);

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var tx = uow.Transaction;
        StockTransferPlaceDto place;
        if (scan.Kind == LabelCode.Location)
        {
            var location = await transfers.FindScannedLocationAsync(scan.Parts[0], scan.Parts[1], tx, cancellationToken);
            PdaMoveRules.EnsurePlace(location is not null, location?.WarehouseInUse == true, location?.LocationInUse == true, null, scanned);
            place = new StockTransferPlaceDto(
                LabelCode.Location, location!.WarehouseId, location.WarehouseCode, location.LocationId, location.LocationCode, null);
        }
        else
        {
            var pallet = await transfers.FindScannedPalletAsync(scan.Parts[0], tx, cancellationToken);
            PdaMoveRules.EnsurePlace(
                pallet is not null, pallet?.WarehouseInUse == true, pallet?.LocationInUse == true, pallet?.Status, scanned);
            place = new StockTransferPlaceDto(
                LabelCode.Pallet, pallet!.WarehouseId, pallet.WarehouseCode, pallet.LocationId, pallet.LocationCode,
                new StockTransferPalletDto(
                    pallet.Id, pallet.PalletNo, pallet.ItemId, pallet.ItemCd, pallet.ItemName, pallet.LotId, pallet.LotNo, pallet.Qty));
        }
        await uow.CommitAsync(cancellationToken);
        return place;
    }

    /// <exception cref="NotFoundException">404 Common-E-006.</exception>
    public async Task<StockTransferDto> GetAsync(long id, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var dto = await ReadAsync(id, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return dto;
    }

    /// <summary>
    /// MM-BR-705~708: numbers the transfer (TR + yymm of the transfer date, for TR and LT alike), issues every line from the
    /// sending side and receives it at the receiving side, and moves the Pallets of Pallet lines (MM-BR-912).
    /// </summary>
    /// <exception cref="DomainException">
    /// 422 Common-E-005 / Common-E-002 for a missing or wrong value, MM-E-705, MM-E-715, MM-E-912, MM-E-710 the consignment
    /// warehouse, MM-E-713 a Location the line cannot use, and the stock errors of <see cref="InventoryService.PostAsync"/>
    /// (MM-E-001, 002, 009, 010, 355, 356, 363, 714, 916).
    /// </exception>
    public async Task<StockTransferDto> CreateAsync(SaveStockTransferRequest request, string user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var tx = uow.Transaction;
        var save = await PrepareAsync(request, tx, cancellationToken);
        var transferNo = await numbers.NextAsync(DocumentNumberFormat.StockTransfer, save.Header.TransferDt, user, tx, cancellationToken);
        var id = await transfers.InsertAsync(transferNo, save.Header, user, tx, cancellationToken);
        await PostAsync(id, transferNo, save, user, tx, cancellationToken);
        var dto = await ReadAsync(id, tx, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return dto;
    }

    /// <summary>
    /// MM-BR-707: reverses every movement of the transfer and moves its Pallets back, then saves the request as the transfer's
    /// new content under the same number.
    /// </summary>
    /// <exception cref="DomainException">
    /// MM-E-002 when the transfer's month is closed, MM-E-912 when a moved Pallet changed since, the stock errors of the reversal
    /// (the receiving side decreases) and the checks of <see cref="CreateAsync"/>.
    /// </exception>
    /// <exception cref="ConcurrencyException">409 when updatedAt is missing or no longer the stored one.</exception>
    /// <exception cref="NotFoundException">404 Common-E-006.</exception>
    public async Task<StockTransferDto> UpdateAsync(long id, SaveStockTransferRequest request, string user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var tx = uow.Transaction;
        var row = await LockAsync(id, request.UpdatedAt ?? default, tx, cancellationToken);
        await UndoAsync(row, user, tx, cancellationToken);
        var save = await PrepareAsync(request, tx, cancellationToken);
        if (!await transfers.UpdateAsync(id, save.Header, row.UpdatedAt, user, tx, cancellationToken))
        {
            throw new ConcurrencyException();
        }
        await PostAsync(id, row.TransferNo, save, user, tx, cancellationToken);
        var dto = await ReadAsync(id, tx, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return dto;
    }

    /// <summary>MM-BR-707: reverses every movement of the transfer, moves its Pallets back and deletes it.</summary>
    /// <exception cref="DomainException">MM-E-002, MM-E-912 and the stock errors of the reversal, as on <see cref="UpdateAsync"/>.</exception>
    /// <exception cref="ConcurrencyException">409 when updatedAt is no longer the stored one.</exception>
    /// <exception cref="NotFoundException">404 Common-E-006.</exception>
    public async Task DeleteAsync(long id, DateTimeOffset updatedAt, string user, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var tx = uow.Transaction;
        var row = await LockAsync(id, updatedAt, tx, cancellationToken);
        await UndoAsync(row, user, tx, cancellationToken);
        if (!await transfers.DeleteAsync(id, row.UpdatedAt, tx, cancellationToken))
        {
            throw new ConcurrencyException();
        }
        await uow.CommitAsync(cancellationToken);
    }

    private async Task<StockTransferRow> LockAsync(long id, DateTimeOffset updatedAt, IDbTransaction tx, CancellationToken cancellationToken)
    {
        var row = await transfers.FindAsync(id, forUpdate: true, tx, cancellationToken) ?? throw new NotFoundException(NotFoundCode, id);
        return row.UpdatedAt == updatedAt ? row : throw new ConcurrencyException();
    }

    // MM-BR-707, MM-BR-004, MM-BR-912: a transfer of a closed month stays as it is. Pallets go back to the sending side, which
    // they can only while each is still as the transfer left it; then every movement is reversed (MM-BR-601), dated today or the
    // transfer date when later
    private async Task UndoAsync(StockTransferRow row, string user, IDbTransaction tx, CancellationToken cancellationToken)
    {
        InventoryClosing.EnsureOpen(await stock.GetCloseYearMonthAsync(tx, cancellationToken), row.TransferDt);
        var today = clock.Today();
        var reversalDt = today < row.TransferDt ? row.TransferDt : today;
        var lines = await transfers.FindLinesAsync(row.Id, tx, cancellationToken);
        var palletLines = lines.Where(l => l.PalletId is not null).ToList();
        if (palletLines.Count > 0)
        {
            var locked = (await pallets.LockAsync([.. palletLines.Select(l => l.PalletId!.Value)], tx, cancellationToken))
                .ToDictionary(p => p.Id, p => p.ToState());
            foreach (var line in palletLines)
            {
                var palletId = line.PalletId!.Value;
                StockTransferRules.EnsurePallet(
                    line.Seq, palletId, locked.GetValueOrDefault(palletId), line.ItemId, line.Qty, row.ToWarehouseId, line.ToLocationId, line.LotId);
            }
        }
        var movementIds = await transfers.FindOpenMovementIdsAsync(row.TransferNo, tx, cancellationToken);
        if (movementIds.Count > 0)
        {
            await inventory.ReverseAsync(movementIds, reversalDt, user, tx, cancellationToken);
        }
        foreach (var line in palletLines)
        {
            await transfers.MovePalletAsync(
                line.PalletId!.Value, row.FromWarehouseId, line.FromLocationId, reversalDt, row.TransferNo, line.Seq, user, tx, cancellationToken);
        }
    }

    // Everything a save checks before it writes: the header, the warehouses (MM-BR-710), each line's item, qty, Locations
    // (MM-BR-713, MM-BR-715) and Pallet (MM-BR-912), and the price and amount of each line (MM-BR-706)
    private async Task<PreparedSave> PrepareAsync(SaveStockTransferRequest request, IDbTransaction tx, CancellationToken cancellationToken)
    {
        var fromId = request.FromWarehouseId ?? throw new DomainException(RequiredCode, "fromWarehouseId");
        // MM-BR-922: a transfer that names no type is LT inside the sending warehouse and TR to another
        var movType = Upper(request.MovType) ?? StockTransferRules.TypeFor(fromId, request.ToWarehouseId);
        if (!StockTransferRules.IsType(movType))
        {
            throw new DomainException(StockTransferRules.InvalidCode, movType, 0);
        }
        var toId = request.ToWarehouseId ?? (movType == MovementType.LocationTransfer
            ? fromId
            : throw new DomainException(RequiredCode, "toWarehouseId"));
        var requested = request.Lines ?? [];
        if (requested.Count == 0)
        {
            throw new DomainException(RequiredCode, "lines");
        }
        var header = new StockTransferHeader(movType, request.TransferDt ?? clock.Today(), fromId, toId, Trim(request.Remarks));

        var warehouses = (await transfers.FindWarehousesAsync([fromId, toId], tx, cancellationToken)).ToDictionary(w => w.Id);
        var from = Warehouse(warehouses, fromId, "fromWarehouseId");
        var to = Warehouse(warehouses, toId, "toWarehouseId");
        WarehouseRules.EnsureUserSelectable([fromId, toId], await warehouseMasters.FindConsignmentAsync(tx, cancellationToken));

        var itemIds = requested.Select((l, i) => l.ItemId ?? throw new DomainException(RequiredCode, $"lines[{i}].itemId")).ToList();
        // inventory.md step 0: every item row in one pass in id order, so the prices read here are the ones the movements take
        var items = (await stock.LockItemsAsync([.. itemIds.Distinct()], tx, cancellationToken)).ToDictionary(i => i.ItemId);
        var (policy, amountDecimals) = await DecimalsAsync(tx, cancellationToken);
        StockTransferRules.EnsurePalletsOnce(requested.Select((l, i) => (i + 1, l.PalletId)));
        var palletIds = requested.Where(l => l.PalletId is not null).Select(l => l.PalletId!.Value).ToList();
        var lockedPallets = palletIds.Count == 0
            ? new Dictionary<long, PalletState>()
            : (await pallets.LockAsync(palletIds, tx, cancellationToken)).ToDictionary(p => p.Id, p => p.ToState());

        var lines = new List<PreparedLine>();
        for (var i = 0; i < requested.Count; i++)
        {
            var seq = i + 1;
            var line = requested[i];
            var item = items.GetValueOrDefault(itemIds[i]) ?? throw new DomainException(InvalidCode, $"lines[{i}].itemId", itemIds[i]);
            var qty = line.Qty ?? throw new DomainException(RequiredCode, $"lines[{i}].qty");
            StockTransferRules.EnsureLineValues(movType, seq, item.ItemCd, qty, item.IsStockManaged && !item.IsKit, fromId, toId);
            policy.EnsureQty(DecimalModule.Mm, qty, $"lines[{i}].qty");

            var lotId = line.LotId;
            var lotNo = Trim(line.LotNo);
            var fromLocationId = line.FromLocationId;
            if (line.PalletId is { } palletId)
            {
                var pallet = lockedPallets.GetValueOrDefault(palletId);
                StockTransferRules.EnsurePallet(seq, palletId, pallet, item.ItemId, qty, fromId, fromLocationId, lotId);
                fromLocationId = pallet!.LocationId;
                lotId = pallet.LotId;
                lotNo = null;
            }
            var fromLocation = await LocationAsync(item, from, fromLocationId, tx, cancellationToken);
            var toLocation = await LocationAsync(item, to, line.ToLocationId, tx, cancellationToken);
            StockTransferRules.EnsureLine(movType, seq, fromId, toId, fromLocation.Id, toLocation.Id);

            var price = item.MovingAverageCost;
            lines.Add(new PreparedLine(
                seq, item.ItemId, qty, lotId, lotNo, fromLocation.Id, toLocation.Id, line.PalletId, price,
                StockTransferRules.Amount(qty, price, amountDecimals)));
        }
        return new PreparedSave(header, lines);
    }

    // MM-BR-705, MM-BR-706, MM-BR-357: the sending side first, at the moving average cost and the rounded amount; the receiving
    // side then takes the Lot each issue used and the same price and amount. Pallets follow their stock (MM-BR-912)
    private async Task PostAsync(long id, string transferNo, PreparedSave save, string user, IDbTransaction tx, CancellationToken cancellationToken)
    {
        var header = save.Header;
        var issued = await inventory.PostAsync(
            [.. save.Lines.Select(l => new StockMovementRequest(
                header.MovType, header.TransferDt, l.ItemId, header.FromWarehouseId, -l.Qty, l.Price, transferNo, l.Seq, l.LotId, l.LotNo,
                -l.Amount, l.FromLocationId, l.PalletId))],
            user, tx, cancellationToken);
        var lotBySeq = issued.ToDictionary(m => m.RefSeq!.Value, m => m.LotId!.Value);
        await inventory.PostAsync(
            [.. save.Lines.Select(l => new StockMovementRequest(
                header.MovType, header.TransferDt, l.ItemId, header.ToWarehouseId, l.Qty, l.Price, transferNo, l.Seq, lotBySeq[l.Seq],
                Amount: l.Amount, LocationId: l.ToLocationId, PalletId: l.PalletId))],
            user, tx, cancellationToken);
        await transfers.InsertLinesAsync(id, [.. save.Lines.Select(l => new NewStockTransferLine(
            l.Seq, l.ItemId, l.Qty, lotBySeq[l.Seq], l.FromLocationId, l.ToLocationId, l.PalletId, l.Price, l.Amount))], user, tx, cancellationToken);
        foreach (var line in save.Lines.Where(l => l.PalletId is not null))
        {
            await transfers.MovePalletAsync(
                line.PalletId!.Value, header.ToWarehouseId, line.ToLocationId, header.TransferDt, transferNo, line.Seq, user, tx, cancellationToken);
        }
    }

    private async Task<StockTransferDto> ReadAsync(long id, IDbTransaction tx, CancellationToken cancellationToken)
    {
        var row = await transfers.FindAsync(id, forUpdate: false, tx, cancellationToken) ?? throw new NotFoundException(NotFoundCode, id);
        var lines = await transfers.FindLinesAsync(id, tx, cancellationToken);
        return new StockTransferDto(row.ToSummary(), lines);
    }

    private static TransferWarehouse Warehouse(Dictionary<long, TransferWarehouse> warehouses, long id, string field)
    {
        var warehouse = warehouses.GetValueOrDefault(id);
        return warehouse is { InUse: true } ? warehouse : throw new DomainException(InvalidCode, field, id);
    }

    // MM-BR-713: null is '*'; another Location must be one of the warehouse, in use, for a Location-managed item
    private async Task<StockLocation> LocationAsync(
        ItemStock item, TransferWarehouse warehouse, long? locationId, IDbTransaction tx, CancellationToken cancellationToken)
    {
        var location = await stock.FindLocationAsync(warehouse.Id, locationId, tx, cancellationToken);
        StockKeyRules.EnsureLocation(
            item.ItemCd, item.LocationManaged, warehouse.Id, warehouse.WarehouseCode, location,
            locationId?.ToString(CultureInfo.InvariantCulture) ?? StockKeyRules.System);
        return location!;
    }

    // BC-BR-421, BC-BR-423: the MM qty decimals and the base currency's amount decimals
    private async Task<(DecimalPolicy Policy, int AmountDecimals)> DecimalsAsync(IDbTransaction tx, CancellationToken cancellationToken)
    {
        var company = await localization.FindCompanyAsync(tx, cancellationToken);
        var baseCurrency = company?.BaseCurrencyCode ?? CountryProfiles.Get(CountryProfiles.Us).BaseCurrencyCode;
        var currencies = await localization.FindCurrenciesAsync(tx, cancellationToken);
        var policy = new DecimalPolicy(
            company?.CountryCode ?? CountryProfiles.Us,
            (await localization.FindDecimalSettingsAsync(tx, cancellationToken))
                .Select(d => new DecimalSetting(d.Module, d.QtyDecimals, Rounding.ParseMode(d.RoundingMode))),
            (await localization.FindCountryDecimalsAsync(tx, cancellationToken))
                .Select(d => new CountryDecimal(d.CountryCode, d.Module, d.UnitPriceDecimals)),
            currencies.ToDictionary(c => c.CurrencyCode, c => c.CountryCode));
        var rounder = new AmountRounder(currencies.Select(c => new Currency(c.CurrencyCode, c.Decimals)));
        return (policy, rounder.DecimalsOf(baseCurrency));
    }

    private static string? Trim(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string? Upper(string? text) => Trim(text)?.ToUpperInvariant();

    private sealed record PreparedLine(
        int Seq, long ItemId, decimal Qty, long? LotId, string? LotNo, long FromLocationId, long ToLocationId, long? PalletId, decimal Price,
        decimal Amount);

    private sealed record PreparedSave(StockTransferHeader Header, IReadOnlyList<PreparedLine> Lines);
}
