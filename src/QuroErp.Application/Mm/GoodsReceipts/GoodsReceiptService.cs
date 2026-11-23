using System.Data;
using QuroErp.Application.Bc.BusinessPlaces;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Bc.Tax;
using QuroErp.Application.Common;
using QuroErp.Application.Common.DocumentNumbers;
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Mm.Inventory;
using QuroErp.Application.Mm.Pallets;
using QuroErp.Application.Mm.Warehouses;
using QuroErp.Contracts.Mm.GoodsReceipts;
using QuroErp.Domain.Bc.BusinessPlaces;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Bc.Tax;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.GoodsReceipts;
using QuroErp.Domain.Mm.Inventory;
using QuroErp.Domain.Mm.Lots;
using QuroErp.Domain.Mm.PurchaseOrders;
using QuroErp.Domain.Mm.Warehouses;

namespace QuroErp.Application.Mm.GoodsReceipts;

/// <summary>
/// Goods receipts (MM-UC-004): list, find, save, delete and the PO lines to import. One save is one transaction: header, lines,
/// number, PO received qty and status, stock movements, Lots and their attributes, Pallets and standard purchase prices
/// (goods-receipt.md "기본 흐름" 5, MM-BR-353, MM-BR-364, MM-BR-910). Every write locks the header row and checks updatedAt (409).
/// The receipt makes no payable; the supplier invoice matches its lines.
/// </summary>
public sealed class GoodsReceiptService(
    IGoodsReceiptRepository receipts,
    ITaxRepository taxes,
    ITaxRateProvider taxRates,
    ILocalizationRepository localization,
    IExchangeRateRepository exchangeRates,
    IBusinessPlaceRepository businessPlaces,
    IWarehouseRepository warehouseMasters,
    IPalletRepository pallets,
    InventoryService inventory,
    DocumentNumberService numbers,
    IBusinessClock clock,
    IUnitOfWorkFactory unitOfWork)
{
    /// <summary>The goods receipt does not exist. Arg: id.</summary>
    public const string NotFoundCode = "Common-E-006";

    public async Task<IReadOnlyList<GoodsReceiptSummaryDto>> SearchAsync(GoodsReceiptSearchQuery query, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var rows = await receipts.SearchAsync(query, GoodsReceiptRoutes.MaxListSize, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return [.. rows.Select(r => new GoodsReceiptSummaryDto(
            r.Id, r.GrNo, GoodsReceiptRules.StatusOf(r.TotGrAmt, r.AnyInvoiced, r.AllInvoiced), r.GrDt, r.VendorId, r.VendorName,
            r.CurrencyCode, r.TotGrAmt))];
    }

    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task<GoodsReceiptDto> GetAsync(long id, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var dto = await ReadAsync(id, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return dto;
    }

    /// <summary>MM-BR-113: PO lines of the vendor the receipt can import.</summary>
    public Task<IReadOnlyList<OpenPoLineDto>> OpenPoLinesAsync(long vendorId, CancellationToken cancellationToken) =>
        OpenPoLinesAsync(vendorId, null, null, cancellationToken);

    /// <summary>
    /// MM-BR-113: PO lines the receipt can import, of the vendor or, for the PDA, of the order with this PO number (MM-BR-920).
    /// With itemId only the order's lines of that scanned item (MM-BR-923).
    /// </summary>
    /// <exception cref="BadRequestException">Common-E-005 without vendorId or poNo.</exception>
    /// <exception cref="DomainException">
    /// MM-E-923 for an unknown PO number or an item with no remaining line on the order; MM-E-024 for an order that is not
    /// approved or partly received, or of another vendor.
    /// </exception>
    public async Task<IReadOnlyList<OpenPoLineDto>> OpenPoLinesAsync(
        long? vendorId, string? poNo, long? itemId, CancellationToken cancellationToken)
    {
        var no = string.IsNullOrWhiteSpace(poNo) ? null : poNo.Trim().ToUpperInvariant();
        if (no is null && vendorId is null)
        {
            throw new BadRequestException(GoodsReceiptRules.RequiredCode, nameof(OpenPoLineDto.VendorId));
        }
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        IReadOnlyList<OpenPoLineDto> lines;
        if (no is null)
        {
            lines = await receipts.FindOpenPoLinesAsync(vendorId, null, uow.Transaction, cancellationToken);
        }
        else
        {
            var po = await receipts.FindPdaPoAsync(no, itemId, uow.Transaction, cancellationToken);
            GoodsReceiptRules.EnsurePdaPo(no, po is not null, po?.Status, po?.VendorId, vendorId);
            lines = await receipts.FindOpenPoLinesAsync(null, no, uow.Transaction, cancellationToken);
            if (itemId is { } id)
            {
                GoodsReceiptRules.EnsureItemOnPo(id, po!.ItemCd, lines.Select(l => l.ItemId));
                lines = [.. lines.Where(l => l.ItemId == id)];
            }
        }
        await uow.CommitAsync(cancellationToken);
        return lines;
    }

    /// <summary>Saves a new receipt with the next GR number of the receipt date's month (MM-BR-106) and receives its stock.</summary>
    /// <exception cref="DomainException">
    /// MM-E-101, 102, 103, 115, 161, 703, 710, MM-E-024; the inventory checks (MM-E-001, 002, 009, 010, 352, 356); Common-E-005,
    /// Common-E-006; BC-E-412, BC-E-513.
    /// </exception>
    /// <exception cref="ConfirmationRequiredException">MM-W-102 in WARN mode until confirmOverReceipt is true (MM-BR-102).</exception>
    /// <exception cref="BadRequestException">400 BC-E-321 when there is no default business place.</exception>
    public async Task<GoodsReceiptDto> CreateAsync(SaveGoodsReceiptRequest request, string user, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var transaction = uow.Transaction;
        var save = await PrepareAsync(request, stored: null, storedLines: [], user, transaction, cancellationToken);
        // BC-BR-321: a new document belongs to the default business place; the screen does not choose it
        var businessPlaceId = await businessPlaces.FindDefaultIdAsync(transaction, cancellationToken)
            ?? throw new BadRequestException(BusinessPlaceRules.NotFoundCode);
        var grNo = await numbers.NextAsync(DocumentNumberFormat.GoodsReceipt, request.GrDt, user, transaction, cancellationToken);
        var id = await receipts.InsertAsync(grNo, businessPlaceId, save.Write, user, transaction, cancellationToken);
        await ApplyAsync(id, grNo, save, user, transaction, cancellationToken);
        var dto = await ReadAsync(id, transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return dto;
    }

    /// <summary>
    /// Changes the reference number, remarks and default warehouse, adds lines, deletes the lines left out (MM-BR-107) and
    /// changes the Lot attributes of saved lines (MM-BR-364). A deleted line takes back its stock and PO received qty and closes
    /// its Pallets (MM-BR-108, MM-BR-111, MM-BR-911). An invoiced or returned receipt takes Lot attribute changes only (MM-BR-104).
    /// </summary>
    /// <exception cref="DomainException">MM-E-104, 107, 116, 117, 911 and the checks of <see cref="CreateAsync"/>.</exception>
    /// <exception cref="ConfirmationRequiredException">MM-W-102 as on <see cref="CreateAsync"/>.</exception>
    /// <exception cref="ConcurrencyException">updatedAt is missing or differs, or a line id is not one of this receipt.</exception>
    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task<GoodsReceiptDto> UpdateAsync(long id, SaveGoodsReceiptRequest request, string user, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var transaction = uow.Transaction;
        var row = await LockAsync(id, request.UpdatedAt ?? default, transaction, cancellationToken);
        var lines = await receipts.FindLinesAsync(id, transaction, cancellationToken);
        if (GoodsReceiptRules.IsLocked(lines.Select(l => l.ToStored())))
        {
            await UpdateLotAttributesAsync(row, lines, request, user, transaction, cancellationToken);
            var updated = await ReadAsync(id, transaction, cancellationToken);
            await uow.CommitAsync(cancellationToken);
            return updated;
        }
        var save = await PrepareAsync(request, row, lines, user, transaction, cancellationToken);
        await ReleaseDeletedLinesAsync(row.GrNo, save, transaction, user, cancellationToken);
        if (!await receipts.UpdateAsync(
                id, save.Write, [.. save.Deleted.Select(l => l.Id)], row.UpdatedAt, user, transaction, cancellationToken))
        {
            throw new ConcurrencyException();
        }
        await ApplyAsync(id, row.GrNo, save, user, transaction, cancellationToken);
        var dto = await ReadAsync(id, transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return dto;
    }

    /// <summary>
    /// Deletes the receipt; every line takes back its stock and PO received qty and closes its Pallets (MM-BR-108, MM-BR-109,
    /// MM-BR-111, MM-BR-911).
    /// </summary>
    /// <exception cref="DomainException">
    /// MM-E-104; MM-E-911 when a Pallet was moved, shipped, split, merged or adjusted; the inventory checks of a decrease
    /// (MM-E-001, 002, 009, 010, 356).
    /// </exception>
    /// <exception cref="ConcurrencyException">updatedAt differs from the stored row.</exception>
    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task DeleteAsync(long id, DateTimeOffset updatedAt, string user, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var transaction = uow.Transaction;
        var row = await LockAsync(id, updatedAt, transaction, cancellationToken);
        var lines = await receipts.FindLinesAsync(id, transaction, cancellationToken);
        GoodsReceiptRules.EnsureEditable(row.GrNo, lines.Select(l => l.ToStored()));
        var poLines = await LockPoLinesAsync(lines.Select(l => l.PurchaseOrderLineId), transaction, cancellationToken);
        var save = new PreparedSave(new GoodsReceiptWrite(Header(row), new(0, 0, 0, 0), []), [], lines, poLines, BaseCurrencyCode: "");
        await ReleaseDeletedLinesAsync(row.GrNo, save, transaction, user, cancellationToken);
        await ApplyAsync(id, row.GrNo, save, user, transaction, cancellationToken);
        if (!await receipts.DeleteAsync(id, updatedAt, transaction, cancellationToken))
        {
            throw new ConcurrencyException();
        }
        await uow.CommitAsync(cancellationToken);
    }

    private async Task<GoodsReceiptRow> LockAsync(long id, DateTimeOffset updatedAt, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var row = await receipts.FindAsync(id, forUpdate: true, transaction, cancellationToken) ?? throw new NotFoundException(NotFoundCode, id);
        return row.UpdatedAt == updatedAt ? row : throw new ConcurrencyException();
    }

    private async Task<IReadOnlyDictionary<long, ReceiptPoLine>> LockPoLinesAsync(
        IEnumerable<long?> purchaseOrderLineIds, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var ids = purchaseOrderLineIds.OfType<long>().Distinct().ToList();
        return ids.Count == 0
            ? new Dictionary<long, ReceiptPoLine>()
            : (await receipts.LockPoLinesAsync(ids, transaction, cancellationToken)).ToDictionary(l => l.Id);
    }

    // Applies the defaults (MM-BR-161, MM-BR-703, BC-BR-542), reads the masters, lets the Domain check and compute, and asks
    // the over-receipt question (MM-BR-102)
    private async Task<PreparedSave> PrepareAsync(
        SaveGoodsReceiptRequest request, GoodsReceiptRow? stored, IReadOnlyList<GoodsReceiptLineRow> storedLines, string user,
        IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var vendor = await receipts.FindVendorAsync(request.VendorId, transaction, cancellationToken)
            ?? throw new DomainException(GoodsReceiptRules.NotFoundCode, request.VendorId);
        var company = await localization.FindCompanyAsync(transaction, cancellationToken);
        var currencies = (await localization.FindCurrenciesAsync(transaction, cancellationToken)).Where(c => c.UseYn);
        var baseCurrency = company?.BaseCurrencyCode ?? CountryProfiles.Get(CountryProfiles.Us).BaseCurrencyCode;
        // BC-BR-424: amounts and tax use the MM module rounding mode; HALF_UP when the setting row is missing
        var mm = (await localization.FindDecimalSettingsAsync(transaction, cancellationToken)).FirstOrDefault(d => d.Module == DecimalModule.Mm);
        var settings = new GoodsReceiptSettings(
            baseCurrency, company?.TaxCalcMode ?? TaxCalcMode.Document, mm is null ? RoundingMode.HalfUp : Rounding.ParseMode(mm.RoundingMode));

        // MM-BR-107: saved lines come back unchanged with their id, except their Lot attributes (MM-BR-364); the ones left out
        // are deleted
        var storedById = storedLines.ToDictionary(l => l.Id);
        var kept = new List<StoredGoodsReceiptLine>();
        foreach (var line in request.Lines.Where(l => l.Id is not null))
        {
            // A line id of another receipt means the screen is stale
            var saved = storedById.GetValueOrDefault(line.Id!.Value) ?? throw new ConcurrencyException();
            var input = Input(line, saved.WarehouseId, saved.TaxCodeId);
            GoodsReceiptRules.EnsureUnchanged(saved.ToStored(), input);
            kept.Add(saved.ToStored() with { Lot = input.Lot });
        }
        var keptIds = kept.Select(l => l.Id).ToHashSet();
        var deleted = storedLines.Where(l => !keptIds.Contains(l.Id)).ToList();
        var addedRequests = request.Lines.Where(l => l.Id is null).ToList();

        // Lock the orders before reading their received qty, so two receipts of one PO line see each other's (MM-BR-102, 108)
        var poLines = await LockPoLinesAsync(
            addedRequests.Select(l => l.PurchaseOrderLineId).Concat(deleted.Select(l => l.PurchaseOrderLineId)), transaction, cancellationToken);
        var (currencyCode, exchangeRate) = await CurrencyAsync(
            request, stored, kept.Count, vendor, baseCurrency,
            addedRequests.Select(l => l.PurchaseOrderLineId).OfType<long>().Select(id => poLines.GetValueOrDefault(id)).OfType<ReceiptPoLine>(),
            transaction, cancellationToken);
        var warehouseId = request.WarehouseId ?? stored?.WarehouseId
            ?? await receipts.FindDefaultWarehouseAsync(user, transaction, cancellationToken)
            ?? throw new DomainException(GoodsReceiptRules.WarehouseCode, 0);
        var header = new GoodsReceiptHeader(
            request.VendorId, request.GrDt, warehouseId, Clean(request.RefNo)?.ToUpperInvariant(), Clean(request.Remarks), currencyCode,
            exchangeRate);
        if (stored is not null)
        {
            GoodsReceiptRules.EnsureHeaderChange(Header(stored), storedLines.Count, header);
        }

        var added = addedRequests.Select(l => Input(l, header.WarehouseId, vendor.TaxCodeId ?? 0)).ToList();
        var warehouseIds = added.Select(l => l.WarehouseId).OfType<long>()
            .Concat(header.WarehouseId == stored?.WarehouseId ? [] : [header.WarehouseId]).Distinct().ToList();
        // MM-BR-710: only the system posts to the consignment warehouse
        WarehouseRules.EnsureUserSelectable(
            warehouseIds.Select(id => (long?)id), await warehouseMasters.FindConsignmentAsync(transaction, cancellationToken));
        var masters = new GoodsReceiptMasters(
            await receipts.FindItemTypesAsync([.. added.Select(l => l.ItemId).Distinct()], transaction, cancellationToken),
            await receipts.FindUsableWarehousesAsync(warehouseIds, transaction, cancellationToken),
            await TaxCodesAsync(kept.Select(l => l.TaxCodeId).Concat(added.Select(l => l.TaxCodeId)), header.GrDt, transaction, cancellationToken),
            poLines,
            await receipts.FindLotManagedItemsAsync(
                [.. kept.Select(l => l.ItemId).Concat(added.Select(l => l.ItemId)).Distinct()], transaction, cancellationToken));

        var rounder = new AmountRounder(currencies.Select(c => new Currency(c.CurrencyCode, c.Decimals)));
        var nextSeq = storedLines.Count == 0 ? 1 : storedLines.Max(l => l.Seq) + 1;
        var (lines, totals) = GoodsReceiptRules.Calculate(header, kept, added, nextSeq, masters, settings, rounder, stored?.WarehouseId);
        await AskOverReceiptAsync(request.ConfirmOverReceipt, lines, poLines, deleted, transaction, cancellationToken);
        return new PreparedSave(new GoodsReceiptWrite(header, totals, lines), [.. lines.Where(l => l.Id is null)], deleted, poLines, baseCurrency);
    }

    // MM-BR-161: the PO's currency and rate; else the request's, the saved receipt's or the vendor's currency and the
    // receipt date's rate (BC-BR-412)
    private async Task<(string CurrencyCode, decimal ExchangeRate)> CurrencyAsync(
        SaveGoodsReceiptRequest request, GoodsReceiptRow? stored, int keptCount, GoodsReceiptVendorRow vendor, string baseCurrency,
        IEnumerable<ReceiptPoLine> poLines, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var requestedCurrency = Clean(request.CurrencyCode)?.ToUpperInvariant();
        var requestedRate = request.ExchangeRate;
        if (requestedCurrency is null && requestedRate is null && stored is not null && keptCount > 0)
        {
            (requestedCurrency, requestedRate) = (stored.CurrencyCode, stored.ExchangeRate);
        }
        var (currency, rate) = GoodsReceiptRules.DocumentCurrency(requestedCurrency, requestedRate, poLines);
        if (currency.Length == 0)
        {
            currency = vendor.CurrencyCode ?? baseCurrency;
        }
        if (rate == 0m)
        {
            var latest = currency == baseCurrency ? null : await exchangeRates.FindLatestAsync(currency, request.GrDt, transaction, cancellationToken);
            rate = ExchangeRateRules.DefaultRate(currency, baseCurrency, latest) ?? throw new DomainException(GoodsReceiptRules.CurrencyCode, currency, 0m);
        }
        return (currency, rate);
    }

    private async Task<IReadOnlyDictionary<long, ReceiptTaxCode>> TaxCodesAsync(
        IEnumerable<long> taxCodeIds, DateOnly grDt, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var codes = await taxes.FindTaxCodesAsync([.. taxCodeIds.Distinct()], transaction, cancellationToken);
        var result = new Dictionary<long, ReceiptTaxCode>();
        foreach (var code in codes.Where(c => c.UseYn))
        {
            result[code.Id] = new ReceiptTaxCode(await taxRates.GetRateAsync(code, grDt, transaction, cancellationToken), code.TaxKind);
        }
        return result;
    }

    // MM-BR-102: DENY refuses, WARN asks until the user confirms. The lines this save deletes give their qty back first
    private async Task AskOverReceiptAsync(
        bool? confirmed, IReadOnlyList<GoodsReceiptLine> lines, IReadOnlyDictionary<long, ReceiptPoLine> poLines,
        IReadOnlyList<GoodsReceiptLineRow> deleted, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var givenBack = deleted.Where(l => l.PurchaseOrderLineId is not null)
            .GroupBy(l => l.PurchaseOrderLineId!.Value).ToDictionary(g => g.Key, g => g.Sum(l => l.GrQty));
        var available = poLines.ToDictionary(
            p => p.Key, p => p.Value with { ReceivedQty = p.Value.ReceivedQty - givenBack.GetValueOrDefault(p.Key) });
        var over = GoodsReceiptRules.OverReceipts(lines, available);
        if (over.Count == 0)
        {
            return;
        }
        var first = over[0];
        var mode = OverReceiptModes.OrDefault(await receipts.FindOverReceiptModeAsync(transaction, cancellationToken));
        if (mode == OverReceiptModes.Deny)
        {
            throw new DomainException(GoodsReceiptRules.OverReceiptCode, first.PoNo, first.Seq, first.RemainingQty);
        }
        if (confirmed != true)
        {
            throw new ConfirmationRequiredException(
                GoodsReceiptRules.OverReceiptWarningCode, GoodsReceiptRules.ConfirmOverReceiptField, first.PoNo, first.Seq, first.RemainingQty);
        }
    }

    // An invoiced or returned receipt (MM-BR-104) takes only new Lot attributes of its saved lines (MM-BR-364)
    private async Task UpdateLotAttributesAsync(
        GoodsReceiptRow row, IReadOnlyList<GoodsReceiptLineRow> lines, SaveGoodsReceiptRequest request, string user,
        IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var storedById = lines.ToDictionary(l => l.Id);
        var inputs = request.Lines
            .Select(l => (l.Id, Line: l.Id is { } lineId && storedById.TryGetValue(lineId, out var saved)
                ? Input(l, saved.WarehouseId, saved.TaxCodeId)
                : Input(l, row.WarehouseId, 0)))
            .ToList();
        var header = new GoodsReceiptHeader(
            request.VendorId, request.GrDt, request.WarehouseId ?? row.WarehouseId, Clean(request.RefNo)?.ToUpperInvariant(),
            Clean(request.Remarks), Clean(request.CurrencyCode)?.ToUpperInvariant() ?? row.CurrencyCode, request.ExchangeRate ?? row.ExchangeRate);
        GoodsReceiptRules.EnsureLotAttributesOnly(row.GrNo, Header(row), header, [.. lines.Select(l => l.ToStored())], inputs);

        var lotManaged = await receipts.FindLotManagedItemsAsync([.. lines.Select(l => l.ItemId).Distinct()], transaction, cancellationToken);
        var writes = inputs.Select(i => (Saved: storedById[i.Id!.Value], i.Line.Lot))
            .Where(x => x.Saved.LotId is not null)
            .Select(x => new ReceiptLotWrite(
                x.Saved.LotId!.Value,
                LotRules.CheckAttributes(
                    x.Saved.Seq, lotManaged.Contains(x.Saved.ItemId), x.Lot?.SupplierLotNo, x.Lot?.MfgDt, x.Lot?.ExpiryDt),
                null))
            .ToList();
        await receipts.SetLotAttributesAsync(writes, user, transaction, cancellationToken);
        if (!await receipts.TouchAsync(row.Id, row.UpdatedAt, user, transaction, cancellationToken))
        {
            throw new ConcurrencyException();
        }
    }

    // MM-BR-911: a deleted line closes its Pallets, which must still be as the receipt made them, before its stock goes back
    // (so the reversal is not held by MM-BR-916) and before the line row goes (the Pallets and Lots point at it)
    private async Task ReleaseDeletedLinesAsync(
        string grNo, PreparedSave save, IDbTransaction transaction, string user, CancellationToken cancellationToken)
    {
        if (save.Deleted.Count == 0)
        {
            return;
        }
        var lineIds = save.Deleted.Select(l => l.Id).ToList();
        var linePallets = await pallets.LockByReceiptLinesAsync(lineIds, transaction, cancellationToken);
        foreach (var pallet in linePallets)
        {
            PalletRules.EnsureAsReceived(pallet.PalletNo, pallet.AsReceived);
        }
        if (linePallets.Count > 0)
        {
            await pallets.CloseForDeletedLinesAsync(
                [.. linePallets.Select(p => p.Id)], ReversalDt(save.Write.Header), grNo, user, transaction, cancellationToken);
        }
        await receipts.UnlinkLotsAsync(lineIds, transaction, cancellationToken);
    }

    // MM-BR-601: a reversal is dated the day it is made, never before the receipt itself
    private DateOnly ReversalDt(GoodsReceiptHeader header)
    {
        var today = clock.Today();
        return today < header.GrDt ? header.GrDt : today;
    }

    // The side effects of a save in the receipt's transaction: stock (MM-BR-111, MM-BR-601), Lots and their attributes
    // (MM-BR-353, MM-BR-364), Pallets (MM-BR-910), PO received qty and status (MM-BR-108, MM-BR-109) and standard purchase
    // prices (MM-BR-110)
    private async Task ApplyAsync(
        long id, string grNo, PreparedSave save, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var header = save.Write.Header;
        // Service lines have no movement to reverse
        var movementIds = save.Deleted.Count == 0
            ? []
            : await receipts.FindOpenMovementIdsAsync(grNo, [.. save.Deleted.Select(l => l.Seq)], transaction, cancellationToken);
        if (movementIds.Count > 0)
        {
            await inventory.ReverseAsync(movementIds, ReversalDt(header), user, transaction, cancellationToken);
        }
        var lotWrites = save.Write.Lines.Where(l => l.Id is not null && l.LotId is not null)
            .Select(l => new ReceiptLotWrite(l.LotId!.Value, l.Lot ?? LotAttributes.None, null))
            .ToList();
        if (save.Added.Count > 0)
        {
            // Service items have no stock; the inventory service skips them (MM-BR-008). A lot-managed item gets a new Lot (MM-BR-353)
            var posted = await inventory.PostAsync(
                [.. save.Added.Select(l => new StockMovementRequest(
                    MovementType.GoodsReceipt, header.GrDt, l.ItemId, l.WarehouseId, l.GrQty, l.StockPrice, grNo, l.Seq,
                    LocationId: l.LocationId))],
                user, transaction, cancellationToken);
            var movements = posted.Where(m => m.LotId is not null && m.RefSeq is not null).ToDictionary(m => m.RefSeq!.Value);
            if (movements.Count > 0)
            {
                await receipts.SetLineKeysAsync(
                    id, movements.ToDictionary(m => m.Key, m => (m.Value.LocationId!.Value, m.Value.LotId!.Value)), transaction, cancellationToken);
                var lineIds = (await receipts.FindLinesAsync(id, transaction, cancellationToken)).ToDictionary(l => l.Seq, l => l.Id);
                var added = save.Added.Where(l => movements.ContainsKey(l.Seq)).ToList();
                lotWrites.AddRange(added.Select(l => new ReceiptLotWrite(
                    movements[l.Seq].LotId!.Value, l.Lot ?? LotAttributes.None, lineIds[l.Seq])));
                await CreatePalletsAsync(grNo, header.GrDt, added, movements, lineIds, user, transaction, cancellationToken);
            }
            // MM-BR-110: prices in the base currency only; a foreign price has no standard price to replace (as MM-BR-016)
            if (header.CurrencyCode == save.BaseCurrencyCode)
            {
                await receipts.SetStandardPricesAsync(
                    save.Added.GroupBy(l => l.ItemId).ToDictionary(g => g.Key, g => g.Last().GrPrc), user, transaction, cancellationToken);
            }
        }

        if (lotWrites.Count > 0)
        {
            await receipts.SetLotAttributesAsync(lotWrites, user, transaction, cancellationToken);
        }

        var qtyByPoLine = save.Added.Where(l => l.PurchaseOrderLineId is not null).Select(l => (Line: l.PurchaseOrderLineId!.Value, Qty: l.GrQty))
            .Concat(save.Deleted.Where(l => l.PurchaseOrderLineId is not null).Select(l => (Line: l.PurchaseOrderLineId!.Value, Qty: -l.GrQty)))
            .GroupBy(x => x.Line).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));
        if (qtyByPoLine.Count == 0)
        {
            return;
        }
        await receipts.AddReceiptQtyAsync(qtyByPoLine, user, transaction, cancellationToken);
        var orderIds = qtyByPoLine.Keys.Where(save.PoLines.ContainsKey).Select(l => save.PoLines[l].PurchaseOrderId).Distinct().ToList();
        foreach (var order in (await receipts.FindPoQuantitiesAsync(orderIds, transaction, cancellationToken)).GroupBy(q => q.PurchaseOrderId))
        {
            if (!PoStatusCode.TryParse(order.First().Status, out var status))
            {
                continue;
            }
            var after = PoStatusRules.AfterReceipt(status, [.. order.Select(q => (q.PoQty, q.ReceivedQty))]);
            if (after != status)
            {
                await receipts.SetPoStatusAsync(order.Key, after, user, transaction, cancellationToken);
            }
        }
    }

    // MM-BR-910: a stock line with a Pallet qty is split into Pallets of that qty on its Location and new Lot, numbered PL + yymm
    // of the receipt date
    private async Task CreatePalletsAsync(
        string grNo, DateOnly grDt, IReadOnlyList<GoodsReceiptLine> added, Dictionary<int, GoodsMovement> movements,
        Dictionary<int, long> lineIds, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var parts = added.Where(l => l.PalletQty is not null)
            .SelectMany(l => PalletRules.Split(l.GrQty, l.PalletQty!.Value).Select(qty => (Line: l, Qty: qty)))
            .ToList();
        if (parts.Count == 0)
        {
            return;
        }
        var palletNos = await numbers.NextAsync(DocumentNumberFormat.Pallet, [.. parts.Select(_ => grDt)], user, transaction, cancellationToken);
        await pallets.InsertAsync(
            [.. parts.Zip(palletNos, (p, no) => new NewPallet(
                no, p.Line.ItemId, movements[p.Line.Seq].LotId!.Value, p.Line.WarehouseId, movements[p.Line.Seq].LocationId!.Value, p.Qty,
                lineIds[p.Line.Seq], grDt, grNo, p.Line.Seq))],
            user, transaction, cancellationToken);
    }

    private async Task<GoodsReceiptDto> ReadAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var r = await receipts.FindAsync(id, forUpdate: false, transaction, cancellationToken) ?? throw new NotFoundException(NotFoundCode, id);
        var lines = await receipts.FindLinesAsync(id, transaction, cancellationToken);
        var palletNos = await pallets.FindNumbersByReceiptAsync(id, transaction, cancellationToken);
        var status = GoodsReceiptRules.StatusOf(r.TotGrAmt, [.. lines.Select(l => (l.GrQty, l.InvoicedQty, l.ReturnedQty))]);
        return new GoodsReceiptDto(
            r.Id, r.GrNo, status, r.VendorId, r.VendorName, r.GrDt, r.WarehouseId, r.RefNo, r.Remarks, r.CurrencyCode, r.ExchangeRate,
            r.SupplyAmt, r.TaxAmt, r.TotGrAmt, r.TotBaseAmt,
            [.. lines.Select(l => new GoodsReceiptLineDto(
                l.Id, l.Seq, l.ItemId, l.ItemCd, l.ItemName, l.Unit, l.WarehouseId, l.WarehouseCode, l.LotId, l.LotNo, l.GrQty, l.GrPrc,
                l.GrAmt, l.TaxCodeId, l.TaxCode, l.SupplyAmt, l.TaxAmt, l.Deductible, l.PurchaseOrderLineId, l.PoNo, l.PoSeq, l.PoQty,
                l.PoReceivedQty, l.InvoicedQty, l.ReturnedQty, l.LocationId, l.LocationCode, l.SupplierLotNo, l.MfgDt, l.ExpiryDt,
                l.PalletQty, palletNos.GetValueOrDefault(l.Id) ?? []))],
            r.UpdatedAt, r.CreatedAt, r.CreatedBy, r.UpdatedBy);
    }

    private static GoodsReceiptHeader Header(GoodsReceiptRow row) =>
        new(row.VendorId, row.GrDt, row.WarehouseId, row.RefNo, row.Remarks, row.CurrencyCode, row.ExchangeRate);

    private static GoodsReceiptLineInput Input(SaveGoodsReceiptLineRequest line, long defaultWarehouseId, long defaultTaxCodeId) => new(
        line.ItemId, Clean(line.ItemName), line.WarehouseId ?? defaultWarehouseId, line.GrQty, line.GrPrc, line.TaxCodeId ?? defaultTaxCodeId,
        line.Deductible, line.PurchaseOrderLineId, line.LocationId, line.PalletQty,
        new LotAttributes(line.SupplierLotNo, line.MfgDt, line.ExpiryDt));

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>A checked save: what to write, the new and deleted lines, the locked PO lines and the base currency.</summary>
    private sealed record PreparedSave(
        GoodsReceiptWrite Write, IReadOnlyList<GoodsReceiptLine> Added, IReadOnlyList<GoodsReceiptLineRow> Deleted,
        IReadOnlyDictionary<long, ReceiptPoLine> PoLines, string BaseCurrencyCode);
}
