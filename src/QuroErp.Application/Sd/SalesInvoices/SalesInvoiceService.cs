using System.Data;
using System.Globalization;
using QuroErp.Application.Bc.BusinessPlaces;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Bc.Tax;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Codes;
using QuroErp.Application.Common.DocumentNumbers;
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Mm.Inventory;
using QuroErp.Application.Mm.Warehouses;
using QuroErp.Application.Sd.Customers;
using QuroErp.Application.Sd.SalesOrders;
using QuroErp.Application.Sd.Shipments;
using QuroErp.Contracts.Sd.SalesInvoices;
using QuroErp.Domain.Bc.BusinessPlaces;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Bc.Tax;
using QuroErp.Domain.Common;
using QuroErp.Domain.Common.Codes;
using QuroErp.Domain.Mm.Inventory;
using QuroErp.Domain.Mm.Warehouses;
using QuroErp.Domain.Sd.Customers;
using QuroErp.Domain.Sd.SalesInvoices;
using QuroErp.Domain.Sd.SalesOrders;
using QuroErp.Domain.Sd.Shipments;

namespace QuroErp.Application.Sd.SalesInvoices;

/// <summary>
/// Sales invoices (SD-UC-003): list, find, save, delete, void, the BILL_THEN_SHIP order lines to import, unshipped cancellation and
/// its undo. One save is one transaction: header, lines, number, billed qty of shipment and order lines, shipment status and, for
/// BILL_THEN_SHIP lines, the consignment moves (sales-invoice.md "기본 흐름" 6, SD-BR-142). Every write locks the header row and
/// checks updatedAt (409).
/// </summary>
public sealed class SalesInvoiceService(
    ISalesInvoiceRepository invoices,
    IShipmentRepository shipments,
    ISalesOrderRepository orders,
    ICustomerRepository customers,
    ITaxRepository taxes,
    ITaxRateProvider taxRates,
    ILocalizationRepository localization,
    IBusinessPlaceRepository businessPlaces,
    IWarehouseRepository warehouseMasters,
    ExchangeRateService exchangeRates,
    InventoryService inventory,
    DocumentNumberService numbers,
    ICodeValidator codes,
    IBusinessClock clock,
    IUnitOfWorkFactory unitOfWork)
{
    /// <summary>The invoice or Credit Memo does not exist. Arg: id.</summary>
    public const string NotFoundCode = "Common-E-006";

    public async Task<IReadOnlyList<SalesInvoiceSummaryDto>> SearchAsync(SalesInvoiceSearchQuery query, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var rows = await invoices.SearchAsync(query, SalesInvoiceRoutes.MaxListSize, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return [.. rows.Select(r => new SalesInvoiceSummaryDto(
            r.Id, r.DnNo, r.Status, r.BillingFlow, r.DnDt, r.CustomerId, r.CustomerName, r.CurrencyCode, r.TotDnAmt, r.BalAmt))];
    }

    /// <exception cref="NotFoundException">Common-E-006, also for a Credit Memo id (SD-BR-120).</exception>
    public async Task<SalesInvoiceDto> GetAsync(long id, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var dto = await ReadAsync(id, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return dto;
    }

    /// <summary>SD-BR-141: BILL_THEN_SHIP order lines of the customer in this currency left to bill.</summary>
    public async Task<IReadOnlyList<OpenBillFirstOrderLineDto>> OpenOrderLinesAsync(
        long customerId, string currencyCode, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var rows = await invoices.FindOpenOrderLinesAsync(customerId, currencyCode.Trim().ToUpperInvariant(), uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return rows;
    }

    /// <summary>
    /// Saves a new invoice with the next DN number (SD-BR-106). Shipment lines add to the shipment and order billed qty (SD-BR-108);
    /// BILL_THEN_SHIP lines add to the order billed qty and move their stock to the consignment warehouse (SD-BR-142, SD-BR-143).
    /// </summary>
    /// <exception cref="DomainException">
    /// SD-E-004, 101, 102, 103, 116, 131, 141, 404, MM-E-703, MM-E-710; the inventory checks (MM-E-001, 002, 009, 010, 355, 356);
    /// Common-E-002 for a code value not in use; Common-E-005, Common-E-006; SD-E-031, BC-E-412, BC-E-423 for the currency and rate.
    /// </exception>
    /// <exception cref="ConfirmationRequiredException">SD-W-009 or SD-W-309 when that question is not answered yet (SD-BR-117, SD-BR-309).</exception>
    /// <exception cref="BadRequestException">400 BC-E-321 when there is no default business place.</exception>
    public async Task<SalesInvoiceDto> CreateAsync(SaveSalesInvoiceRequest request, string user, CancellationToken cancellationToken)
    {
        await EnsureCodesAsync(request, cancellationToken);
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var transaction = uow.Transaction;
        var save = await PrepareAsync(request, stored: null, storedLines: [], user, transaction, cancellationToken);
        // BC-BR-321: a new document belongs to the default business place; the screen does not choose it
        var businessPlaceId = await businessPlaces.FindDefaultIdAsync(transaction, cancellationToken)
            ?? throw new BadRequestException(BusinessPlaceRules.NotFoundCode);
        var dnNo = await numbers.NextAsync(DocumentNumberFormat.SalesInvoice, save.Header.DnDt, user, transaction, cancellationToken);
        var (id, _) = await invoices.InsertAsync(dnNo, businessPlaceId, save.Header, save.AddedWrites, user, transaction, cancellationToken);
        await ApplyAsync(id, dnNo, save, user, transaction, cancellationToken);
        var dto = await ReadAsync(id, transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return dto;
    }

    /// <summary>
    /// Changes the header, adds lines and deletes the lines left out (SD-BR-107). A deleted line takes back its billed qty and, for
    /// BILL_THEN_SHIP, its consignment move (SD-BR-108, SD-BR-144).
    /// </summary>
    /// <exception cref="DomainException">SD-E-104, 107, 119, 136, 144 and the checks of <see cref="CreateAsync"/>.</exception>
    /// <exception cref="ConfirmationRequiredException">As on <see cref="CreateAsync"/>.</exception>
    /// <exception cref="ConcurrencyException">updatedAt is missing or differs, the invoice is void, or a line id is not one of this invoice.</exception>
    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task<SalesInvoiceDto> UpdateAsync(long id, SaveSalesInvoiceRequest request, string user, CancellationToken cancellationToken)
    {
        await EnsureCodesAsync(request, cancellationToken);
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var transaction = uow.Transaction;
        var row = await LockAsync(id, request.UpdatedAt ?? default, transaction, cancellationToken);
        SalesInvoiceRules.EnsureSameCustomer(row.CustomerId, request.CustomerId);
        SalesInvoiceRules.EnsureOpen(row.DnNo, row.TotDnAmt, row.BalAmt);
        var lines = await invoices.FindLinesAsync(id, transaction, cancellationToken);
        var save = await PrepareAsync(request, row, lines, user, transaction, cancellationToken);
        SalesInvoiceRules.EnsureDeletable(row.DnNo, row.BillingFlow, save.Deleted);
        _ = await invoices.UpdateAsync(
                id, save.Header, [.. save.Deleted.Select(l => l.Id)], save.AddedWrites, row.UpdatedAt, user, transaction, cancellationToken)
            ?? throw new ConcurrencyException();
        await ApplyAsync(id, row.DnNo, save, user, transaction, cancellationToken);
        var dto = await ReadAsync(id, transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return dto;
    }

    /// <summary>Deletes the invoice and takes back what its lines billed and moved (SD-BR-108, SD-BR-144).</summary>
    /// <exception cref="DomainException">SD-E-104, SD-E-113 for an invoice of a SHIP_AND_BILL shipment, SD-E-144; the inventory checks of a reversal.</exception>
    /// <exception cref="ConcurrencyException">updatedAt differs from the stored row, or the invoice is void.</exception>
    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task DeleteAsync(long id, DateTimeOffset updatedAt, string user, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var transaction = uow.Transaction;
        var row = await LockAsync(id, updatedAt, transaction, cancellationToken);
        var lines = (await invoices.FindLinesAsync(id, transaction, cancellationToken)).Select(l => l.ToStored()).ToList();
        SalesInvoiceRules.EnsureVoidable(row.DnNo, row.BillingFlow, row.TotDnAmt, row.BalAmt, lines);
        await TakeBackAsync(row, lines, user, transaction, cancellationToken);
        if (!await invoices.DeleteAsync(id, row.UpdatedAt, transaction, cancellationToken))
        {
            throw new ConcurrencyException();
        }
        await uow.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// SD-BR-113, SD-BR-144: takes back the billed qty and consignment moves of every line, voids the invoice (VO, amounts 0) and
    /// recalculates the shipment status (SD-BR-110).
    /// </summary>
    /// <exception cref="DomainException">SD-E-104, SD-E-113, SD-E-144; the inventory checks of a reversal.</exception>
    /// <exception cref="ConcurrencyException">updatedAt differs from the stored row, or the invoice is void.</exception>
    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task<SalesInvoiceDto> VoidAsync(long id, DateTimeOffset updatedAt, string user, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var transaction = uow.Transaction;
        var row = await LockAsync(id, updatedAt, transaction, cancellationToken);
        var lines = (await invoices.FindLinesAsync(id, transaction, cancellationToken)).Select(l => l.ToStored()).ToList();
        SalesInvoiceRules.EnsureVoidable(row.DnNo, row.BillingFlow, row.TotDnAmt, row.BalAmt, lines);
        await TakeBackAsync(row, lines, user, transaction, cancellationToken);
        if (!await invoices.VoidAsync(id, row.UpdatedAt, user, transaction, cancellationToken))
        {
            throw new ConcurrencyException();
        }
        var dto = await ReadAsync(id, transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return dto;
    }

    /// <summary>
    /// SD-BR-145: cancels unshipped qty of BILL_THEN_SHIP lines. The stock goes back from the consignment warehouse to the line
    /// warehouse (TR, same lot), the line's cancelled qty goes up, the order's billed qty down, and one Credit Memo is made at the
    /// invoice's prices after discount, tax rates (BC-BR-546), currency and rate (SD-BR-231).
    /// </summary>
    /// <exception cref="DomainException">SD-E-145; Common-E-005, Common-E-006; the inventory checks (MM-E-001, 002, 009, 010, 356).</exception>
    /// <exception cref="ConcurrencyException">updatedAt differs from the stored row, or the invoice is void.</exception>
    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task<CancelUnshippedResult> CancelUnshippedAsync(
        long id, CancelUnshippedRequest request, string user, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var transaction = uow.Transaction;
        var row = await LockAsync(id, request.UpdatedAt, transaction, cancellationToken);
        var lines = (await invoices.FindLinesAsync(id, transaction, cancellationToken)).Select(l => l.ToStored()).ToList();
        var cancels = SalesInvoiceRules.CancelLines(
            row.DnNo, row.BillingFlow, row.Status, lines, [.. request.Lines.Select(l => new UnshippedCancelInput(l.SalesInvoiceLineId, l.Qty))]);
        await LockOrderLinesAsync(cancels.Select(c => c.Line.SalesOrderLineId), transaction, cancellationToken);
        var consignment = await ConsignmentAsync(transaction, cancellationToken);

        var (settings, rounder) = await AmountSettingsAsync(transaction, cancellationToken);
        var rates = await TaxRatesAsync(cancels.Select(c => c.Line.TaxCodeId), row.DnDt, transaction, cancellationToken);
        var (amounts, totals) = ShipmentInvoice.Calculate(
            [.. cancels.Select((c, i) => new ShipmentInvoiceInput(i + 1, c.Qty, SalesInvoiceRules.NetPrice(c.Line.DnPrc, c.Line.DcRate), 0m, c.Line.TaxCodeId))],
            row.CurrencyCode, row.ExchangeRate, rates, settings, rounder);
        var cmNo = await numbers.NextAsync(DocumentNumberFormat.CreditMemo, request.CancelDt, user, transaction, cancellationToken);
        var reasons = Enumerable.Range(0, SalesInvoiceRules.ReasonCount).Select(i => request.Reasons is { } r && i < r.Count && r[i]).ToList();
        var cmLines = cancels.Select((c, i) => new CreditMemoLineWrite(
            i + 1, c.Line.ItemId, c.Line.ItemName, -c.Qty, SalesInvoiceRules.NetPrice(c.Line.DnPrc, c.Line.DcRate), -amounts[i].DnAmt,
            c.Line.WarehouseId, c.Line.TaxCodeId, -amounts[i].SupplyAmt, -amounts[i].TaxAmt, c.Line.SalesOrderLineId, c.Line.LotId, c.Line.Id)).ToList();
        var creditMemoId = await invoices.InsertCreditMemoAsync(
            cmNo,
            new CreditMemoHeader(
                row.CustomerId, request.CancelDt, row.CurrencyCode, row.ExchangeRate, row.WarehouseId, row.BusinessPlaceId, reasons,
                -totals.SupplyAmt, -totals.TaxAmt, -totals.TotDnAmt, -totals.TotBaseAmt, row.DnNo),
            cmLines, user, transaction, cancellationToken);

        // The stock goes back the way it came, under the Credit Memo number (SD-BR-145, MM-BR-357)
        await inventory.PostAsync(
            [.. cancels.Select((c, i) => new StockMovementRequest(
                MovementType.StockTransfer, request.CancelDt, c.Line.ItemId, consignment.Id, -c.Qty, null, cmNo, i + 1, c.Line.LotId))],
            user, transaction, cancellationToken);
        await inventory.PostAsync(
            [.. cancels.Select((c, i) => new StockMovementRequest(
                MovementType.StockTransfer, request.CancelDt, c.Line.ItemId, c.Line.WarehouseId, c.Qty, null, cmNo, i + 1, c.Line.LotId))],
            user, transaction, cancellationToken);
        await invoices.AddCancelledQtyAsync(Sum(cancels.Select(c => (c.Line.Id, c.Qty))), user, transaction, cancellationToken);
        await AddOrderDnQtyAsync(cancels.Select(c => (c.Line.SalesOrderLineId, -c.Qty)), user, transaction, cancellationToken);
        if (!await invoices.TouchAsync(id, row.UpdatedAt, user, transaction, cancellationToken))
        {
            throw new ConcurrencyException();
        }
        await uow.CommitAsync(cancellationToken);
        return new CancelUnshippedResult(creditMemoId, cmNo);
    }

    /// <summary>
    /// SD-BR-146: undoes an unshipped cancellation. Its Credit Memo is deleted, the stock moves back to the consignment warehouse
    /// (reversal of the cancellation's TR), and the cancelled qty and order billed qty are restored.
    /// </summary>
    /// <param name="updatedAt">The invoice's updatedAt the screen read; null skips the check.</param>
    /// <exception cref="DomainException">SD-E-146 when the Credit Memo is applied; SD-E-141 when the order line was billed again meanwhile; the inventory checks of a reversal.</exception>
    /// <exception cref="ConcurrencyException">updatedAt differs from the stored row, or the invoice is void.</exception>
    /// <exception cref="NotFoundException">Common-E-006 when the invoice or a cancellation Credit Memo of it does not exist.</exception>
    public async Task UndoCancellationAsync(long id, long creditMemoId, DateTimeOffset? updatedAt, string user, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var transaction = uow.Transaction;
        var row = await invoices.FindAsync(id, forUpdate: true, transaction, cancellationToken) ?? throw new NotFoundException(NotFoundCode, id);
        if ((updatedAt is { } expected && expected != row.UpdatedAt) || row.Status == SalesInvoiceStatus.Void)
        {
            throw new ConcurrencyException();
        }
        var (cm, cmLines) = await invoices.FindCancellationAsync(id, creditMemoId, forUpdate: true, transaction, cancellationToken)
            ?? throw new NotFoundException(NotFoundCode, creditMemoId);
        SalesInvoiceRules.EnsureCreditNotApplied(cm.CmNo, cm.TotDnAmt, cm.BalAmt);

        var lines = (await invoices.FindLinesAsync(id, transaction, cancellationToken)).ToDictionary(l => l.Id);
        var restored = cmLines.Select(l => (Line: lines[l.CancelledInvoiceLineId], l.Qty)).ToList();
        var orderLines = await LockOrderLinesAsync(restored.Select(r => r.Line.SalesOrderLineId), transaction, cancellationToken);
        foreach (var group in restored.Where(r => r.Line.SalesOrderLineId is not null).GroupBy(r => r.Line.SalesOrderLineId!.Value))
        {
            var order = orderLines[group.Key];
            if (group.Sum(r => r.Qty) > order.RemainingQty)
            {
                throw new DomainException(SalesInvoiceRules.BillFirstCode, order.SoNo, order.Seq, order.RemainingQty);
            }
        }

        await ReverseTransfersAsync(cm.CmNo, [.. cmLines.Select(l => l.Seq)], cm.CmDt, user, transaction, cancellationToken);
        await invoices.AddCancelledQtyAsync(Sum(restored.Select(r => (r.Line.Id, -r.Qty))), user, transaction, cancellationToken);
        await AddOrderDnQtyAsync(restored.Select(r => (r.Line.SalesOrderLineId, r.Qty)), user, transaction, cancellationToken);
        await invoices.DeleteCreditMemoAsync(creditMemoId, transaction, cancellationToken);
        await invoices.TouchAsync(id, null, user, transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
    }

    // Code fields the request sends must be code values in use (codes.md)
    private async Task EnsureCodesAsync(SaveSalesInvoiceRequest r, CancellationToken cancellationToken)
    {
        await codes.EnsureValidAsync(CodeMajors.SalesRep, Clean(r.Rep), cancellationToken);
        await codes.EnsureValidAsync(CodeMajors.Terms, Clean(r.Terms), cancellationToken);
        await codes.EnsureValidAsync(CodeMajors.ShipmentVia, Clean(r.ShipVia), cancellationToken);
        await codes.EnsureValidAsync(CodeMajors.FreightCharge, Clean(r.FreightCharge), cancellationToken);
        await codes.EnsureValidAsync(CodeMajors.State, Clean(r.ShipState)?.ToUpperInvariant(), cancellationToken);
    }

    // A void invoice cannot change any more; the screen that still shows it as open is stale
    private async Task<SalesInvoiceRow> LockAsync(long id, DateTimeOffset updatedAt, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var row = await invoices.FindAsync(id, forUpdate: true, transaction, cancellationToken) ?? throw new NotFoundException(NotFoundCode, id);
        return row.UpdatedAt == updatedAt && row.Status != SalesInvoiceStatus.Void ? row : throw new ConcurrencyException();
    }

    private async Task<ConsignmentWarehouse> ConsignmentAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        await warehouseMasters.FindConsignmentAsync(transaction, cancellationToken)
        ?? throw new DomainException(WarehouseRules.ConsignmentCode, "-");

    private async Task<IReadOnlyDictionary<long, InvoiceShipmentSource>> LockShipmentLinesAsync(
        IEnumerable<long?> shipmentLineIds, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var ids = shipmentLineIds.OfType<long>().Distinct().ToList();
        return ids.Count == 0
            ? new Dictionary<long, InvoiceShipmentSource>()
            : (await invoices.LockShipmentLinesAsync(ids, transaction, cancellationToken)).ToDictionary(l => l.Id);
    }

    private async Task<IReadOnlyDictionary<long, InvoiceOrderSource>> LockOrderLinesAsync(
        IEnumerable<long?> salesOrderLineIds, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var ids = salesOrderLineIds.OfType<long>().Distinct().ToList();
        return ids.Count == 0
            ? new Dictionary<long, InvoiceOrderSource>()
            : (await invoices.LockOrderLinesAsync(ids, transaction, cancellationToken)).ToDictionary(l => l.Id);
    }

    // Base currency, tax unit and the SD rounding mode (BC-BR-424, BC-BR-543), and the currency decimals (BC-BR-421)
    private async Task<(ShipmentSettings Settings, AmountRounder Rounder)> AmountSettingsAsync(
        IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var company = await localization.FindCompanyAsync(transaction, cancellationToken);
        var sd = (await localization.FindDecimalSettingsAsync(transaction, cancellationToken)).FirstOrDefault(d => d.Module == DecimalModule.Sd);
        var settings = new ShipmentSettings(
            company?.BaseCurrencyCode ?? CountryProfiles.Get(CountryProfiles.Us).BaseCurrencyCode, company?.TaxCalcMode ?? TaxCalcMode.Document,
            sd is null ? RoundingMode.HalfUp : Rounding.ParseMode(sd.RoundingMode));
        var rounder = new AmountRounder(
            (await localization.FindCurrenciesAsync(transaction, cancellationToken)).Where(c => c.UseYn).Select(c => new Currency(c.CurrencyCode, c.Decimals)));
        return (settings, rounder);
    }

    // Applies the defaults, reads and locks the sources, lets the Domain check and compute, and asks the save questions
    private async Task<PreparedSave> PrepareAsync(
        SaveSalesInvoiceRequest request, SalesInvoiceRow? stored, IReadOnlyList<SalesInvoiceLineRow> storedLines, string user,
        IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var customer = await customers.FindAsync(request.CustomerId, forUpdate: false, transaction, cancellationToken)
            ?? throw new DomainException(SalesInvoiceRules.NotFoundCode, request.CustomerId);
        var c = customer.Fields;
        var company = await localization.FindCompanyAsync(transaction, cancellationToken);
        var (settings, rounder) = await AmountSettingsAsync(transaction, cancellationToken);

        // SD-BR-107: saved lines come back unchanged with their id; the ones left out are deleted
        var storedById = storedLines.ToDictionary(l => l.Id);
        var kept = new List<StoredInvoiceLine>();
        foreach (var line in request.Lines.Where(l => l.Id is not null))
        {
            // A line id of another invoice means the screen is stale
            var saved = (storedById.GetValueOrDefault(line.Id!.Value) ?? throw new ConcurrencyException()).ToStored();
            SalesInvoiceRules.EnsureUnchanged(saved, Input(line));
            kept.Add(saved);
        }
        var keptIds = kept.Select(l => l.Id).ToHashSet();
        var deleted = storedLines.Where(l => !keptIds.Contains(l.Id)).Select(l => l.ToStored()).ToList();
        var addedRequests = request.Lines.Where(l => l.Id is null).Select(Input).ToList();

        // Shipments first, then orders, so two invoices of the same sources lock them in the same order (SD-BR-102, SD-BR-141)
        var shipmentLines = await LockShipmentLinesAsync(
            addedRequests.Select(l => l.ShipmentLineId).Concat(deleted.Select(l => l.ShipmentLineId)), transaction, cancellationToken);
        var orderLines = await LockOrderLinesAsync(
            addedRequests.Where(l => l.ShipmentLineId is null).Select(l => l.SalesOrderLineId)
                .Concat(deleted.Where(l => l.ShipmentLineId is null).Select(l => l.SalesOrderLineId)),
            transaction, cancellationToken);
        var addedShipments = addedRequests.Where(l => l.ShipmentLineId is not null)
            .Select(l => shipmentLines.GetValueOrDefault(l.ShipmentLineId!.Value)).OfType<InvoiceShipmentSource>().ToList();
        var addedOrders = addedRequests.Where(l => l.ShipmentLineId is null && l.SalesOrderLineId is not null)
            .Select(l => orderLines.GetValueOrDefault(l.SalesOrderLineId!.Value)).OfType<InvoiceOrderSource>().ToList();
        var billingFlow = SalesInvoiceRules.BillingFlowOf(
            stored?.BillingFlow,
            kept.Any(l => l.ShipmentLineId is not null) || addedRequests.Any(l => l.ShipmentLineId is not null),
            kept.Any(IsBillFirst) || addedRequests.Any(l => l.ShipmentLineId is null && l.SalesOrderLineId is not null));

        var (currencyCode, exchangeRate) = await CurrencyAsync(
            request, stored, customer, settings.BaseCurrencyCode,
            addedShipments.Select(s => (s.CurrencyCode, s.ExchangeRate)).Concat(addedOrders.Select(o => (o.CurrencyCode, o.ExchangeRate))).ToList(),
            transaction, cancellationToken);
        SalesInvoiceRules.EnsureCurrency(currencyCode, addedShipments.Select(s => s.CurrencyCode).Concat(addedOrders.Select(o => o.CurrencyCode)));
        CheckRate(currencyCode, exchangeRate, settings, rounder);

        // MM-BR-703, MM-BR-710: the default warehouse of BILL_THEN_SHIP and direct lines, never the consignment warehouse
        var consignment = await warehouseMasters.FindConsignmentAsync(transaction, cancellationToken);
        var warehouseId = request.WarehouseId ?? stored?.WarehouseId
            ?? await shipments.FindDefaultWarehouseAsync(user, transaction, cancellationToken)
            ?? throw new DomainException(ShipmentRules.WarehouseCode, 0);
        WarehouseRules.EnsureUserSelectable(
            addedRequests.Where(l => l.ShipmentLineId is null).Select(l => l.WarehouseId)
                .Concat(warehouseId == stored?.WarehouseId ? [] : [warehouseId]),
            consignment);

        var dnDt = stored?.DnDt ?? request.DnDt;
        var shipTo = ShipTo(request, stored is null ? c : null);
        var isKr = company?.CountryCode == CountryProfiles.Kr;
        var priceLevel = Clean(c.PriceLevel);
        var items = (await orders.FindItemsByIdAsync(
                [.. addedRequests.Select(l => l.ItemId).Concat(addedShipments.Select(s => s.ItemId)).Concat(addedOrders.Select(o => o.ItemId)).Distinct()],
                priceLevel, transaction, cancellationToken))
            .ToDictionary(i => i.Id);
        var exemptCodeId = await orders.FindTaxCodeIdOfKindAsync(isKr ? TaxKind.VatExempt : TaxKind.SalesTaxExempt, transaction, cancellationToken);
        TaxExemptionDefault? exemption = null;
        if (!isKr && shipTo.ShipState is { } state)
        {
            var certificates = await taxes.FindExemptionsAsync(customer.Id, state, transaction, cancellationToken);
            exemption = TaxExemptionDefault.For(certificates.Select(x => new TaxExemptionCertificate(x.CertificateNo, x.StateCode, x.ValidTo)), state, dnDt);
        }
        var defaultTaxCodes = items.Values.ToDictionary(
            i => i.Id,
            i => SalesOrderDefaults.TaxCode(c.TaxCodeId, exemptCodeId, isKr ? i.VatKind != "EXEMPT" : i.Taxable, exemption).TaxCodeId ?? 0);

        var rates = await TaxRatesAsync(
            addedRequests.Select(l => l.TaxCodeId ?? 0).Concat(defaultTaxCodes.Values).Concat(kept.Select(l => l.TaxCodeId))
                .Concat(addedShipments.Select(s => s.TaxCodeId)).Concat(addedOrders.Select(o => o.TaxCodeId)),
            dnDt, transaction, cancellationToken);
        var usable = await shipments.FindUsableWarehousesAsync(
            [.. addedRequests.Select(l => l.WarehouseId).OfType<long>().Concat(addedShipments.Select(s => s.WarehouseId)).Append(warehouseId).Distinct()],
            transaction, cancellationToken);
        var masters = new SalesInvoiceMasters(
            items.ToDictionary(i => i.Key, i => i.Value.ItemType ?? string.Empty), items.ToDictionary(i => i.Key, i => i.Value.ItemCd),
            await orders.FindDiscountRatesAsync(transaction, cancellationToken), rates.Keys.ToHashSet(), usable, defaultTaxCodes);

        var nextSeq = storedLines.Count == 0 ? 1 : storedLines.Max(l => l.Seq) + 1;
        var added = SalesInvoiceRules.Lines(
            addedRequests, nextSeq, billingFlow == Customer.ShipAndBill, request.CustomerId, warehouseId, shipmentLines, orderLines, masters);
        SalesInvoiceRules.EnsureUnbilled(added, deleted, shipmentLines, ShipmentStatus.Void);
        SalesInvoiceRules.EnsureRemaining(added, deleted, orderLines, SalesOrderStatus.Closed);

        // SD-BR-109, SD-BR-132: the whole invoice is recalculated at the invoice date's rates
        var (amounts, totals) = ShipmentInvoice.Calculate(
            [.. kept.Select(l => new ShipmentInvoiceInput(l.Seq, l.DnQty, l.DnPrc, l.DcRate, l.TaxCodeId))
                .Concat(added.Select(l => new ShipmentInvoiceInput(l.Seq, l.DnQty, l.DnPrc, l.DcRate, l.TaxCodeId))).OrderBy(l => l.Seq)],
            currencyCode, exchangeRate, rates, settings, rounder);
        var bySeq = amounts.ToDictionary(a => a.Seq);
        var addedWrites = added.Select(l => new SalesInvoiceLineWrite(l, bySeq[l.Seq].DnAmt, bySeq[l.Seq].SupplyAmt, bySeq[l.Seq].TaxAmt)).ToList();

        var terms = Clean(request.Terms) ?? (stored is null ? Clean(c.Terms) : null);
        var header = new SalesInvoiceHeader(
            request.CustomerId, stored?.SalesOrderId ?? addedOrders.Select(o => (long?)o.SalesOrderId).FirstOrDefault(), dnDt,
            request.DueDt ?? stored?.DueDt ?? SalesOrderDefaults.DueDate(terms, dnDt), currencyCode, exchangeRate, warehouseId, billingFlow,
            Clean(request.Rep) ?? (stored is null ? Clean(c.Rep) : null), terms, Clean(request.ShipVia), shipTo, Clean(request.Remarks),
            // SD-BR-133: the certificate the user entered, else the one a line was exempted by
            Clean(request.TaxExemptionCertNo) ?? (exemption is { Exempt: true } && exemptCodeId is { } exempt && kept.Select(l => l.TaxCodeId).Concat(added.Select(l => l.TaxCodeId)).Contains(exempt)
                ? exemption.CertificateNo
                : stored?.TaxExemptionCertNo));
        if (string.IsNullOrWhiteSpace(header.Rep))
        {
            throw new DomainException(SalesInvoiceRules.RequiredCode, "rep");
        }
        if (string.IsNullOrWhiteSpace(header.ShipTo.ShipZipCode))
        {
            throw new DomainException(SalesInvoiceRules.RequiredCode, "shipZipCode");
        }

        await AskAsync(request, customer, header, totals, stored, user, transaction, cancellationToken);
        return new PreparedSave(header, added, addedWrites, kept, deleted, shipmentLines, orderLines, amounts, totals, consignment);
    }

    // SD-BR-131: a saved invoice keeps its currency; shipments and orders give it; an invoice of direct lines takes the request's or
    // the customer's currency and the rate of the invoice date (BC-BR-412)
    private async Task<(string CurrencyCode, decimal ExchangeRate)> CurrencyAsync(
        SaveSalesInvoiceRequest request, SalesInvoiceRow? stored, CustomerRow customer, string baseCurrency,
        List<(string CurrencyCode, decimal ExchangeRate)> sources, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        if (stored is not null)
        {
            return (stored.CurrencyCode, stored.ExchangeRate);
        }
        if (sources.Count > 0)
        {
            return sources[0];
        }
        var currency = Clean(request.CurrencyCode)?.ToUpperInvariant() ?? Clean(customer.Fields.CurrencyCode) ?? baseCurrency;
        var rate = request.ExchangeRate
            ?? (currency == baseCurrency ? 1m : await exchangeRates.DefaultRateAsync(currency, request.DnDt, transaction, cancellationToken))
            ?? throw new DomainException(SalesOrderEntryRules.CurrencyCode, currency, "0");
        return (currency, rate);
    }

    // BC-BR-412: a known currency, rate above zero and 1 for the base currency
    private static void CheckRate(string currencyCode, decimal exchangeRate, ShipmentSettings settings, AmountRounder rounder)
    {
        rounder.DecimalsOf(currencyCode);
        if (exchangeRate <= 0 || (currencyCode == settings.BaseCurrencyCode && exchangeRate != 1m))
        {
            throw new DomainException(SalesOrderEntryRules.CurrencyCode, currencyCode, exchangeRate.ToString(CultureInfo.InvariantCulture));
        }
        ExchangeRate.EnsureDecimals(exchangeRate, "exchangeRate");
    }

    private async Task<IReadOnlyDictionary<long, TaxCodeRate>> TaxRatesAsync(
        IEnumerable<long> taxCodeIds, DateOnly date, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var rates = new Dictionary<long, TaxCodeRate>();
        foreach (var code in (await taxes.FindTaxCodesAsync([.. taxCodeIds.Where(id => id > 0).Distinct()], transaction, cancellationToken)).Where(x => x.UseYn))
        {
            rates[code.Id] = await taxRates.GetRateAsync(code, date, transaction, cancellationToken);
        }
        return rates;
    }

    // SD-BR-117 (as SD-BR-009) and SD-BR-309: an unanswered question → 422 with its confirmField and nothing saved; yes to the ship-to
    // question also updates the customer in this transaction. Direct lines are items without stock, which have no price level
    // (SD-BR-010, SD-BR-408), and the other lines keep their source price, so the price level question never comes up here
    private async Task AskAsync(
        SaveSalesInvoiceRequest request, CustomerRow customer, SalesInvoiceHeader header, ShipmentInvoiceTotals totals, SalesInvoiceRow? stored,
        string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var c = customer.Fields;
        var customerShipTo = new SalesOrderShipTo(
            c.ShipName, c.ShipAddr1, c.ShipAddr2, c.ShipCity, c.ShipState, c.ShipZipCode, c.ShipCountry, c.ShipPhone, c.ShipFax, c.FreightCharge);
        if (request.UpdateCustomerShipTo != false && SalesOrderEntryRules.ShipToDiffers(header.ShipTo, customerShipTo))
        {
            if (request.UpdateCustomerShipTo is null)
            {
                throw new ConfirmationRequiredException(SalesOrderEntryRules.ShipToWarningCode, SalesOrderEntryRules.UpdateCustomerShipToField);
            }
            await orders.UpdateCustomerShipToAsync(customer.Id, header.ShipTo, user, transaction, cancellationToken);
        }
        if (request.ConfirmCreditLimit is null
            && SalesOrderEntryRules.CreditLimitExceeded(c.CreditLimit, customer.BalAmt, totals.TotBaseAmt, stored?.TotBaseAmt ?? 0m))
        {
            throw new ConfirmationRequiredException(
                SalesOrderEntryRules.CreditLimitWarningCode, SalesOrderEntryRules.ConfirmCreditLimitField, c.CreditLimit!.Value,
                customer.BalAmt + totals.TotBaseAmt - (stored?.TotBaseAmt ?? 0m));
        }
    }

    // The side effects of a save in the invoice's transaction: amounts (SD-BR-109), consignment moves (SD-BR-142, SD-BR-144), billed
    // qty (SD-BR-108, SD-BR-143) and the shipment status (SD-BR-110)
    private async Task ApplyAsync(long id, string dnNo, PreparedSave save, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        await invoices.UpdateAmountsAsync(id, save.Amounts, save.Totals, user, transaction, cancellationToken);
        await ReverseTransfersAsync(
            dnNo, [.. save.Deleted.Where(IsBillFirst).Select(l => l.Seq)], save.Header.DnDt, user, transaction, cancellationToken);
        var billFirst = save.Added.Where(l => l.ShipmentLineId is null && l.SalesOrderLineId is not null).ToList();
        if (billFirst.Count > 0)
        {
            var consignment = save.Consignment ?? throw new DomainException(WarehouseRules.ConsignmentCode, "-");
            var issued = await inventory.PostAsync(
                [.. billFirst.Select(l => new StockMovementRequest(
                    MovementType.StockTransfer, save.Header.DnDt, l.ItemId, l.WarehouseId, -l.DnQty, null, dnNo, l.Seq, l.LotId, l.LotNo))],
                user, transaction, cancellationToken);
            // MM-BR-357: the lot leaves the line warehouse and arrives in the consignment warehouse
            var lots = issued.Where(m => m.LotId is not null && m.RefSeq is not null).GroupBy(m => m.RefSeq!.Value)
                .ToDictionary(g => g.Key, g => g.First().LotId!.Value);
            await inventory.PostAsync(
                [.. billFirst.Select(l => new StockMovementRequest(
                    MovementType.StockTransfer, save.Header.DnDt, l.ItemId, consignment.Id, l.DnQty, null, dnNo, l.Seq,
                    lots.TryGetValue(l.Seq, out var lotId) ? lotId : null))],
                user, transaction, cancellationToken);
            if (lots.Count > 0)
            {
                await invoices.SetLineLotsAsync(id, lots, transaction, cancellationToken);
            }
        }
        await TakeBackQtyAsync(
            save.Added.Select(l => (l.ShipmentLineId, l.SalesOrderLineId, l.DnQty))
                .Concat(save.Deleted.Select(l => (l.ShipmentLineId, l.SalesOrderLineId, -l.DnQty))),
            save.ShipmentLines, user, transaction, cancellationToken);
    }

    // Delete and void: the lines' billed qty and consignment moves go back (SD-BR-108, SD-BR-113, SD-BR-144)
    private async Task TakeBackAsync(
        SalesInvoiceRow row, IReadOnlyList<StoredInvoiceLine> lines, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var shipmentLines = await LockShipmentLinesAsync(lines.Select(l => l.ShipmentLineId), transaction, cancellationToken);
        await LockOrderLinesAsync(lines.Where(l => l.ShipmentLineId is null).Select(l => l.SalesOrderLineId), transaction, cancellationToken);
        await ReverseTransfersAsync(row.DnNo, [.. lines.Where(IsBillFirst).Select(l => l.Seq)], row.DnDt, user, transaction, cancellationToken);
        await TakeBackQtyAsync(lines.Select(l => (l.ShipmentLineId, l.SalesOrderLineId, -l.DnQty)), shipmentLines, user, transaction, cancellationToken);
    }

    // MM-BR-601: a reversal is dated the day it is made, never before the document itself
    private async Task ReverseTransfersAsync(
        string refKey, IReadOnlyCollection<int> seqs, DateOnly documentDt, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        if (seqs.Count == 0)
        {
            return;
        }
        var movementIds = await invoices.FindOpenTransferIdsAsync(refKey, seqs, transaction, cancellationToken);
        if (movementIds.Count > 0)
        {
            var today = clock.Today();
            await inventory.ReverseAsync(movementIds, today < documentDt ? documentDt : today, user, transaction, cancellationToken);
        }
    }

    // Billed qty of shipment lines (SD-BR-108), of order lines (SD-BR-108, SD-BR-143), then the status of the shipments touched
    // (SD-BR-110, SD-BR-166). Order status is the shipments' (SD-BR-012)
    private async Task TakeBackQtyAsync(
        IEnumerable<(long? ShipmentLineId, long? SalesOrderLineId, decimal Qty)> changes, IReadOnlyDictionary<long, InvoiceShipmentSource> shipmentLines,
        string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var list = changes.ToList();
        var billedQty = Sum(list.Where(c => c.ShipmentLineId is not null).Select(c => (c.ShipmentLineId!.Value, c.Qty)));
        if (billedQty.Count > 0)
        {
            await invoices.AddShipmentBilledQtyAsync(billedQty, user, transaction, cancellationToken);
        }
        await AddOrderDnQtyAsync(list.Select(c => (c.SalesOrderLineId, c.Qty)), user, transaction, cancellationToken);

        var shipmentIds = billedQty.Keys.Select(l => shipmentLines.GetValueOrDefault(l)?.ShipmentId).OfType<long>().Distinct().ToList();
        if (shipmentIds.Count == 0)
        {
            return;
        }
        foreach (var shipment in (await invoices.FindShipmentBillingAsync(shipmentIds, transaction, cancellationToken)).GroupBy(b => b.ShipmentId))
        {
            var current = shipment.First().Status;
            var status = ShipmentStatus.FromBilling(shipment.Select(b => (b.ShipQty, b.BilledQty)));
            if (current != ShipmentStatus.Void && status != current)
            {
                await invoices.SetShipmentStatusAsync(shipment.Key, status, user, transaction, cancellationToken);
            }
        }
    }

    private async Task AddOrderDnQtyAsync(
        IEnumerable<(long? SalesOrderLineId, decimal Qty)> changes, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var dnQty = Sum(changes.Where(c => c.SalesOrderLineId is not null).Select(c => (c.SalesOrderLineId!.Value, c.Qty)));
        if (dnQty.Count > 0)
        {
            await shipments.AddOrderDnQtyAsync(dnQty, user, transaction, cancellationToken);
        }
    }

    private async Task<SalesInvoiceDto> ReadAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var r = await invoices.FindAsync(id, forUpdate: false, transaction, cancellationToken) ?? throw new NotFoundException(NotFoundCode, id);
        var lines = await invoices.FindLinesAsync(id, transaction, cancellationToken);
        var applied = await invoices.FindAppliedAsync(id, transaction, cancellationToken);
        var credit = await invoices.FindAvailableCreditAsync(r.CustomerId, transaction, cancellationToken);
        var cancellations = await invoices.FindCancellationsAsync(id, transaction, cancellationToken);
        var s = r.ShipTo;
        return new SalesInvoiceDto(
            r.Id, r.DnNo, r.Status, r.BillingFlow, r.CustomerId, r.CustomerName, r.SalesOrderId, r.SoNo, r.DnDt, r.DueDt, r.WarehouseId, r.CurrencyCode,
            r.ExchangeRate, r.Rep, r.Terms, r.ShipVia, s.FreightCharge, s.ShipName, s.ShipAddr1, s.ShipAddr2, s.ShipCity, s.ShipState, s.ShipZipCode,
            s.ShipCountry, s.ShipPhone, s.ShipFax, r.Remarks, r.TaxExemptionCertNo, r.SupplyAmt, r.TaxAmt, r.TotDnAmt, r.TotBaseAmt, r.BalAmt,
            applied.PaymentApplied, applied.CreditApplied, credit, r.ShipmentId, r.ShipNo,
            [.. lines.Select(l => new SalesInvoiceLineDto(
                l.Id, l.Seq, l.ItemId, l.ItemCd, l.ItemName, l.Unit, l.WarehouseId, l.WarehouseCode, l.LotId, l.LotNo, l.DnQty, l.DnPrc, l.DnAmt,
                l.DcCd, l.DcRate, l.StdPrc, l.TaxCodeId, l.SupplyAmt, l.TaxAmt, l.SalesOrderLineId, l.SoNo, l.SoSeq, l.ShipmentLineId, l.ShipNo,
                l.ShipSeq, l.ShippedQty, l.CancelledQty, IsBillFirst(l.ToStored()) ? l.DnQty - l.ShippedQty - l.CancelledQty : 0m))],
            [.. cancellations.Select(x => new SalesInvoiceCancellationDto(x.Id, x.CmNo, x.CmDt, x.TotDnAmt, x.BalAmt, x.TotDnAmt != x.BalAmt))],
            r.UpdatedAt, r.CreatedAt, r.CreatedBy, r.UpdatedBy);
    }

    // A BILL_THEN_SHIP line bills an order line without a shipment line (SD-BR-141)
    private static bool IsBillFirst(StoredInvoiceLine line) => line.ShipmentLineId is null && line.SalesOrderLineId is not null;

    private static Dictionary<long, decimal> Sum(IEnumerable<(long Key, decimal Qty)> changes) =>
        changes.GroupBy(c => c.Key).Select(g => (g.Key, Qty: g.Sum(c => c.Qty))).Where(x => x.Qty != 0).ToDictionary(x => x.Key, x => x.Qty);

    // The request's ship-to; a new invoice whose ship-to is all empty takes the customer's (customer is null on update)
    private static SalesOrderShipTo ShipTo(SaveSalesInvoiceRequest r, CustomerFields? customer)
    {
        var shipTo = new SalesOrderShipTo(
            Clean(r.ShipName), Clean(r.ShipAddr1), Clean(r.ShipAddr2), Clean(r.ShipCity), Clean(r.ShipState)?.ToUpperInvariant(), Clean(r.ShipZipCode),
            Clean(r.ShipCountry), Clean(r.ShipPhone), Clean(r.ShipFax), Clean(r.FreightCharge));
        if (customer is null)
        {
            return shipTo;
        }
        var empty = shipTo with { FreightCharge = null } == new SalesOrderShipTo(null, null, null, null, null, null, null, null, null, null);
        return empty
            ? new SalesOrderShipTo(
                Clean(customer.ShipName), Clean(customer.ShipAddr1), Clean(customer.ShipAddr2), Clean(customer.ShipCity), Clean(customer.ShipState),
                Clean(customer.ShipZipCode), Clean(customer.ShipCountry), Clean(customer.ShipPhone), Clean(customer.ShipFax),
                shipTo.FreightCharge ?? Clean(customer.FreightCharge))
            : shipTo with { FreightCharge = shipTo.FreightCharge ?? Clean(customer.FreightCharge) };
    }

    private static SalesInvoiceLineInput Input(SaveSalesInvoiceLineRequest l) => new(
        l.Id, l.ItemId, Clean(l.ItemName), l.DnQty, l.DnPrc, l.DcCd is null ? null : Clean(l.DcCd) ?? "", l.TaxCodeId, l.WarehouseId, l.LotId,
        Clean(l.LotNo)?.ToUpperInvariant(), l.ShipmentLineId, l.SalesOrderLineId);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// A checked save: header, new lines (and with their amounts), kept and deleted saved lines, the locked source lines, every
    /// line's amounts, the totals and the consignment warehouse.
    /// </summary>
    private sealed record PreparedSave(
        SalesInvoiceHeader Header, IReadOnlyList<SalesInvoiceLine> Added, IReadOnlyList<SalesInvoiceLineWrite> AddedWrites,
        IReadOnlyList<StoredInvoiceLine> Kept, IReadOnlyList<StoredInvoiceLine> Deleted, IReadOnlyDictionary<long, InvoiceShipmentSource> ShipmentLines,
        IReadOnlyDictionary<long, InvoiceOrderSource> OrderLines, IReadOnlyList<ShipmentInvoiceLineAmounts> Amounts, ShipmentInvoiceTotals Totals,
        ConsignmentWarehouse? Consignment);
}
