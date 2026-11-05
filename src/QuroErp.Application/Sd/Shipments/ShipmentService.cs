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
using QuroErp.Contracts.Sd.Shipments;
using QuroErp.Domain.Bc.BusinessPlaces;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Bc.Tax;
using QuroErp.Domain.Common;
using QuroErp.Domain.Common.Codes;
using QuroErp.Domain.Mm.Inventory;
using QuroErp.Domain.Mm.Warehouses;
using QuroErp.Domain.Sd.Customers;
using QuroErp.Domain.Sd.SalesOrders;
using QuroErp.Domain.Sd.Shipments;

namespace QuroErp.Application.Sd.Shipments;

/// <summary>
/// Shipments (SD-UC-051): list, find, save, void and the lines a shipment or invoice imports. One save is one transaction: header,
/// lines, number, stock issue, order shipped qty and status and, for SHIP_AND_BILL, the invoice (shipment.md "기본 흐름" 5, 6).
/// Every write locks the header row and checks updatedAt (409). Returns (SD-BR-561) come with the sales return task.
/// </summary>
public sealed class ShipmentService(
    IShipmentRepository shipments,
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
    /// <summary>The shipment does not exist. Arg: id.</summary>
    public const string NotFoundCode = "Common-E-006";

    public async Task<IReadOnlyList<ShipmentSummaryDto>> SearchAsync(ShipmentSearchQuery query, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var rows = await shipments.SearchAsync(query, ShipmentRoutes.MaxListSize, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return [.. rows.Select(r => new ShipmentSummaryDto(
            r.Id, r.ShipNo, r.Status, r.BillingFlow, r.ShipDt, r.CustomerId, r.CustomerName, r.CurrencyCode, r.SalesInvoiceNo))];
    }

    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task<ShipmentDto> GetAsync(long id, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var dto = await ReadAsync(id, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return dto;
    }

    /// <summary>SD-BR-172: the customer's open SHIP_AND_BILL and SHIP_THEN_BILL orders with their lines left to ship.</summary>
    public async Task<IReadOnlyList<OpenSalesOrderDto>> OpenOrderLinesAsync(long customerId, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var rows = await shipments.FindOpenOrderLinesAsync(customerId, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return [.. rows.GroupBy(r => r.SalesOrderId).Select(g =>
        {
            var o = g.First();
            return new OpenSalesOrderDto(
                o.SalesOrderId, o.SoNo, o.SoDt, o.CurrencyCode, o.ExchangeRate, o.BillingFlow, o.Rep, o.ShipVia, o.ShipName, o.ShipAddr1,
                o.ShipAddr2, o.ShipCity, o.ShipState, o.ShipZipCode, o.ShipCountry, o.ShipPhone,
                [.. g.Select(l => new OpenSalesOrderLineDto(
                    l.SalesOrderLineId, l.Seq, l.ItemId, l.ItemCd, l.ItemName, l.Unit, l.SoQty, l.ShipQty, l.SoQty - l.ShipQty, l.SoPrc, l.DcCd,
                    l.DcRate, l.TaxCodeId))]);
        })];
    }

    /// <summary>SD-BR-115: SHIP_THEN_BILL lines of the customer in this currency left to bill.</summary>
    public async Task<IReadOnlyList<UnbilledShipmentLineDto>> UnbilledLinesAsync(long customerId, string currencyCode, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var rows = await shipments.FindUnbilledLinesAsync(customerId, currencyCode.Trim().ToUpperInvariant(), uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return rows;
    }

    /// <summary>SD-BR-175: BILL_THEN_SHIP invoice lines of the customer left to ship.</summary>
    public async Task<IReadOnlyList<UnshippedInvoiceLineDto>> UnshippedInvoiceLinesAsync(long customerId, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var rows = await shipments.FindUnshippedInvoiceLinesAsync(customerId, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return rows;
    }

    /// <summary>Saves a new shipment with the next SH number (SD-BR-164), issues its stock and, for SHIP_AND_BILL, makes its invoice.</summary>
    /// <exception cref="DomainException">
    /// SD-E-004, 031, 035, 114, 161, 162, 170, 174, 175, MM-E-703, MM-E-710; the inventory checks (MM-E-001, 002, 009, 010, 355,
    /// 356); Common-E-002 for a code value not in use; Common-E-005, Common-E-006; BC-E-412, BC-E-423.
    /// </exception>
    /// <exception cref="ConfirmationRequiredException">SD-W-309 until confirmCreditLimit is true (SD-BR-309).</exception>
    /// <exception cref="BadRequestException">400 BC-E-321 when there is no default business place.</exception>
    public async Task<ShipmentDto> CreateAsync(SaveShipmentRequest request, string user, CancellationToken cancellationToken)
    {
        await EnsureCodesAsync(request, cancellationToken);
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var transaction = uow.Transaction;
        var save = await PrepareAsync(request, stored: null, storedLines: [], user, transaction, cancellationToken);
        // BC-BR-321: a new document belongs to the default business place; the screen does not choose it
        var businessPlaceId = await businessPlaces.FindDefaultIdAsync(transaction, cancellationToken)
            ?? throw new BadRequestException(BusinessPlaceRules.NotFoundCode);
        var shipNo = await numbers.NextAsync(DocumentNumberFormat.Shipment, save.Header.ShipDt, user, transaction, cancellationToken);
        var (id, lineIds) = await shipments.InsertAsync(
            shipNo, businessPlaceId, save.Header, ShipmentStatus.Open, save.Added, user, transaction, cancellationToken);
        await ApplyAsync(id, shipNo, stored: null, businessPlaceId, save, lineIds, user, transaction, cancellationToken);
        var dto = await ReadAsync(id, transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return dto;
    }

    /// <summary>
    /// Changes the warehouse default, ship-to and remarks, adds lines and deletes the lines left out (SD-BR-171). A deleted line takes
    /// back its stock and shipped qty (SD-BR-163, SD-BR-165, SD-BR-177); a SHIP_AND_BILL invoice follows (SD-BR-167).
    /// </summary>
    /// <exception cref="DomainException">SD-E-023, 104, 107, 168 and the checks of <see cref="CreateAsync"/>.</exception>
    /// <exception cref="ConfirmationRequiredException">SD-W-309 as on <see cref="CreateAsync"/>.</exception>
    /// <exception cref="ConcurrencyException">updatedAt is missing or differs, the shipment is void, or a line id is not one of this shipment.</exception>
    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task<ShipmentDto> UpdateAsync(long id, SaveShipmentRequest request, string user, CancellationToken cancellationToken)
    {
        await EnsureCodesAsync(request, cancellationToken);
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var transaction = uow.Transaction;
        var row = await LockAsync(id, request.UpdatedAt ?? default, transaction, cancellationToken);
        SalesOrderEntryRules.EnsureSameCustomer(row.CustomerId, request.CustomerId);
        await EnsureInvoiceOpenAsync(row, transaction, cancellationToken);
        var lines = await shipments.FindLinesAsync(id, transaction, cancellationToken);
        var save = await PrepareAsync(request, row, lines, user, transaction, cancellationToken);
        ShipmentRules.EnsureNotBilled(row.BillingFlow, row.ShipNo, save.Deleted);
        var lineIds = await shipments.UpdateAsync(
                id, save.Header, [.. save.Deleted.Select(l => l.Id)], save.Added, row.UpdatedAt, user, transaction, cancellationToken)
            ?? throw new ConcurrencyException();
        await ApplyAsync(id, row.ShipNo, row, businessPlaceId: null, save, lineIds, user, transaction, cancellationToken);
        var dto = await ReadAsync(id, transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return dto;
    }

    /// <summary>
    /// SD-BR-169, SD-BR-177: takes back the stock and shipped qty of every line, voids the shipment and recalculates the order
    /// status; a SHIP_AND_BILL invoice is voided too (SD-BR-113).
    /// </summary>
    /// <exception cref="DomainException">SD-E-104, SD-E-168; the inventory checks of a reversal (MM-E-002, 356).</exception>
    /// <exception cref="ConcurrencyException">updatedAt differs from the stored row, or the shipment is void.</exception>
    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task<ShipmentDto> VoidAsync(long id, DateTimeOffset updatedAt, string user, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var transaction = uow.Transaction;
        var row = await LockAsync(id, updatedAt, transaction, cancellationToken);
        var lines = (await shipments.FindLinesAsync(id, transaction, cancellationToken)).Select(l => l.ToStored()).ToList();
        ShipmentRules.EnsureNotBilled(row.BillingFlow, row.ShipNo, lines);
        await EnsureInvoiceOpenAsync(row, transaction, cancellationToken);
        // Invoices before orders, as a save locks them (SD-BR-177)
        await LockInvoiceLinesAsync(lines.Select(l => l.SalesInvoiceLineId), transaction, cancellationToken);
        var orderLines = await LockOrderLinesAsync(lines.Select(l => l.SalesOrderLineId), transaction, cancellationToken);

        await ReverseAsync(row.ShipNo, row, lines, row.ShipDt, user, transaction, cancellationToken);
        await TakeBackQtyAsync(row.BillingFlow, [], lines, orderLines, user, transaction, cancellationToken);
        if (row.BillingFlow == Customer.ShipAndBill && row.SalesInvoiceId is { } invoiceId)
        {
            await shipments.VoidInvoiceAsync(invoiceId, user, transaction, cancellationToken);
        }
        if (!await shipments.VoidAsync(id, row.UpdatedAt, user, transaction, cancellationToken))
        {
            throw new ConcurrencyException();
        }
        var dto = await ReadAsync(id, transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return dto;
    }

    // Code fields the request sends must be code values in use (codes.md)
    private async Task EnsureCodesAsync(SaveShipmentRequest r, CancellationToken cancellationToken)
    {
        await codes.EnsureValidAsync(CodeMajors.ShipmentVia, Clean(r.ShipVia), cancellationToken);
        await codes.EnsureValidAsync(CodeMajors.State, Clean(r.ShipState)?.ToUpperInvariant(), cancellationToken);
    }

    // A void shipment cannot change any more; the screen that still shows it as open is stale
    private async Task<ShipmentRow> LockAsync(long id, DateTimeOffset updatedAt, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var row = await shipments.FindAsync(id, forUpdate: true, transaction, cancellationToken) ?? throw new NotFoundException(NotFoundCode, id);
        return row.UpdatedAt == updatedAt && row.Status != ShipmentStatus.Void ? row : throw new ConcurrencyException();
    }

    // SD-BR-104, SD-BR-167: a SHIP_AND_BILL shipment changes only while its invoice has no receipt or credit applied
    private async Task EnsureInvoiceOpenAsync(ShipmentRow row, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        if (row.BillingFlow != Customer.ShipAndBill || row.SalesInvoiceId is not { } invoiceId)
        {
            return;
        }
        var invoice = await shipments.FindInvoiceAsync(invoiceId, forUpdate: true, transaction, cancellationToken);
        if (invoice is not null)
        {
            ShipmentRules.EnsureInvoiceOpen(invoice.DnNo, invoice.TotDnAmt, invoice.BalAmt);
        }
    }

    private async Task<IReadOnlyDictionary<long, ShipmentOrderLine>> LockOrderLinesAsync(
        IEnumerable<long?> salesOrderLineIds, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var ids = salesOrderLineIds.OfType<long>().Distinct().ToList();
        return ids.Count == 0
            ? new Dictionary<long, ShipmentOrderLine>()
            : (await shipments.LockOrderLinesAsync(ids, transaction, cancellationToken)).ToDictionary(l => l.Id);
    }

    private async Task<IReadOnlyDictionary<long, ShipmentInvoiceLine>> LockInvoiceLinesAsync(
        IEnumerable<long?> salesInvoiceLineIds, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var ids = salesInvoiceLineIds.OfType<long>().Distinct().ToList();
        return ids.Count == 0
            ? new Dictionary<long, ShipmentInvoiceLine>()
            : (await shipments.LockInvoiceLinesAsync(ids, transaction, cancellationToken)).ToDictionary(l => l.Id);
    }

    // Applies the defaults (MM-BR-703, SD-BR-170, SD-BR-174), reads and locks the sources, lets the Domain check, and asks the
    // credit limit question (SD-BR-309)
    private async Task<PreparedSave> PrepareAsync(
        SaveShipmentRequest request, ShipmentRow? stored, IReadOnlyList<ShipmentLineRow> storedLines, string user,
        IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var customer = await customers.FindAsync(request.CustomerId, forUpdate: false, transaction, cancellationToken)
            ?? throw new DomainException(ShipmentRules.NotFoundCode, request.CustomerId);
        var company = await localization.FindCompanyAsync(transaction, cancellationToken);
        var baseCurrency = company?.BaseCurrencyCode ?? CountryProfiles.Get(CountryProfiles.Us).BaseCurrencyCode;
        // BC-BR-424: amounts and tax use the SD module rounding mode; HALF_UP when the setting row is missing
        var sd = (await localization.FindDecimalSettingsAsync(transaction, cancellationToken)).FirstOrDefault(d => d.Module == DecimalModule.Sd);
        var settings = new ShipmentSettings(
            baseCurrency, company?.TaxCalcMode ?? TaxCalcMode.Document, sd is null ? RoundingMode.HalfUp : Rounding.ParseMode(sd.RoundingMode));
        var rounder = new AmountRounder(
            (await localization.FindCurrenciesAsync(transaction, cancellationToken)).Where(c => c.UseYn).Select(c => new Currency(c.CurrencyCode, c.Decimals)));

        // SD-BR-171: saved lines come back unchanged with their id; the ones left out are deleted
        var storedById = storedLines.ToDictionary(l => l.Id);
        var kept = new List<StoredShipmentLine>();
        foreach (var line in request.Lines.Where(l => l.Id is not null))
        {
            // A line id of another shipment means the screen is stale
            var saved = (storedById.GetValueOrDefault(line.Id!.Value) ?? throw new ConcurrencyException()).ToStored();
            ShipmentRules.EnsureUnchanged(saved, Input(line, saved.WarehouseId, saved.TaxCodeId) with
            {
                ShipPrc = line.ShipPrc ?? saved.ShipPrc,
                SalesOrderLineId = line.SalesOrderLineId ?? saved.SalesOrderLineId,
                SalesInvoiceLineId = line.SalesInvoiceLineId ?? saved.SalesInvoiceLineId,
            });
            kept.Add(saved);
        }
        var keptIds = kept.Select(l => l.Id).ToHashSet();
        var deleted = storedLines.Where(l => !keptIds.Contains(l.Id)).Select(l => l.ToStored()).ToList();
        var addedRequests = request.Lines.Where(l => l.Id is null).ToList();

        // Invoices first, then orders: a BILL_THEN_SHIP invoice line names the order line it bills (SD-BR-176). Locking them
        // before reading their qty makes two shipments of one line see each other's (SD-BR-162, SD-BR-175)
        var invoiceLines = await LockInvoiceLinesAsync(
            addedRequests.Select(l => l.SalesInvoiceLineId).Concat(deleted.Select(l => l.SalesInvoiceLineId)), transaction, cancellationToken);
        var orderLines = await LockOrderLinesAsync(
            addedRequests.Where(l => l.SalesInvoiceLineId is null).Select(l => l.SalesOrderLineId)
                .Concat(deleted.Select(l => l.SalesOrderLineId))
                .Concat(invoiceLines.Values.Select(l => l.SalesOrderLineId)),
            transaction, cancellationToken);
        var orderSources = addedRequests.Where(l => l.SalesInvoiceLineId is null && l.SalesOrderLineId is not null)
            .Select(l => orderLines.GetValueOrDefault(l.SalesOrderLineId!.Value)).OfType<ShipmentOrderLine>().ToList();
        var invoiceSources = addedRequests.Where(l => l.SalesInvoiceLineId is not null)
            .Select(l => invoiceLines.GetValueOrDefault(l.SalesInvoiceLineId!.Value)).OfType<ShipmentInvoiceLine>().ToList();
        var billingFlow = ShipmentRules.BillingFlowOf(
            stored?.BillingFlow, orderSources, addedRequests.Any(l => l.SalesInvoiceLineId is not null),
            addedRequests.Any(l => l.SalesInvoiceLineId is null && l.SalesOrderLineId is null),
            Clean(customer.Fields.BillingFlow) ?? Customer.ShipAndBill);

        var (currencyCode, exchangeRate) = await CurrencyAsync(
            request, stored, customer, baseCurrency, orderSources.Select(o => (o.CurrencyCode, o.ExchangeRate))
                .Concat(invoiceSources.Select(i => (i.CurrencyCode, i.ExchangeRate))).ToList(),
            transaction, cancellationToken);
        ShipmentRules.EnsureCurrency(currencyCode, orderSources.Select(o => o.CurrencyCode).Concat(invoiceSources.Select(i => i.CurrencyCode)));
        CheckRate(currencyCode, exchangeRate, settings, rounder);

        // MM-BR-710: only the system issues from the consignment warehouse, so a BILL_THEN_SHIP shipment takes it (SD-BR-176)
        var consignment = await warehouseMasters.FindConsignmentAsync(transaction, cancellationToken);
        var warehouseId = billingFlow == Customer.BillThenShip && consignment is not null
            ? consignment.Id
            : request.WarehouseId ?? stored?.WarehouseId
              ?? await shipments.FindDefaultWarehouseAsync(user, transaction, cancellationToken)
              ?? throw new DomainException(ShipmentRules.WarehouseCode, 0);
        var added = addedRequests.Select(l => Input(l, warehouseId, l.TaxCodeId ?? customer.Fields.TaxCodeId ?? 0)).ToList();
        if (billingFlow != Customer.BillThenShip)
        {
            WarehouseRules.EnsureUserSelectable(
                added.Select(l => l.WarehouseId).Concat(warehouseId == stored?.WarehouseId ? [] : [warehouseId]), consignment);
        }

        var itemIds = added.Select(l => l.ItemId).Concat(orderSources.Select(o => o.ItemId)).Concat(invoiceSources.Select(i => i.ItemId)).Distinct().ToList();
        var itemCodes = await shipments.FindItemCodesAsync(itemIds, transaction, cancellationToken);
        var usable = await shipments.FindUsableWarehousesAsync(
            [.. added.Select(l => l.WarehouseId).OfType<long>().Append(warehouseId).Distinct()], transaction, cancellationToken);
        var shipDt = stored?.ShipDt ?? request.ShipDt;
        var rates = await TaxRatesAsync(
            added.Select(l => l.TaxCodeId).Concat(kept.Select(l => l.TaxCodeId)).Concat(orderSources.Select(o => o.TaxCodeId))
                .Concat(invoiceSources.Select(i => i.TaxCodeId)),
            shipDt, transaction, cancellationToken);
        var nextSeq = storedLines.Count == 0 ? 1 : storedLines.Max(l => l.Seq) + 1;
        var lines = ShipmentRules.Lines(
            added, nextSeq, billingFlow, request.CustomerId, orderLines, invoiceLines, itemCodes.Keys.ToHashSet(), usable, rates.Keys.ToHashSet(),
            consignment?.Id);
        ShipmentRules.EnsureRemaining(lines, deleted, orderLines);
        ShipmentRules.EnsureUnshipped(lines, deleted, invoiceLines, ShipmentStatus.Void);

        var header = new ShipmentHeader(
            request.CustomerId, stored?.SalesOrderId ?? lines.Select(l => l.SalesOrderLineId).OfType<long>()
                .Select(id => orderLines.GetValueOrDefault(id)?.SalesOrderId).FirstOrDefault(id => id is not null),
            shipDt, warehouseId, currencyCode, exchangeRate, billingFlow, Clean(request.ShipVia), ShipTo(request), Clean(request.Remarks));
        AskCreditLimit(request, customer, header, lines, rates, settings, rounder);
        return new PreparedSave(header, lines, kept, deleted, orderLines, invoiceLines, rates, settings, rounder, itemCodes, Clean(customer.Fields.Terms));
    }

    // SD-BR-170: a saved shipment keeps its currency; the order or invoice gives it; a direct shipment takes the request's or the
    // customer's currency and the latest rate (BC-BR-412)
    private async Task<(string CurrencyCode, decimal ExchangeRate)> CurrencyAsync(
        SaveShipmentRequest request, ShipmentRow? stored, CustomerRow customer, string baseCurrency,
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
            ?? (currency == baseCurrency ? 1m : await exchangeRates.DefaultRateAsync(currency, request.ShipDt, transaction, cancellationToken))
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
        foreach (var code in (await taxes.FindTaxCodesAsync([.. taxCodeIds.Where(id => id > 0).Distinct()], transaction, cancellationToken)).Where(c => c.UseYn))
        {
            rates[code.Id] = await taxRates.GetRateAsync(code, date, transaction, cancellationToken);
        }
        return rates;
    }

    // SD-BR-309: the customer's balance plus what this shipment bills goes over the credit limit. A BILL_THEN_SHIP shipment was
    // billed by its invoice, so it adds nothing
    private static void AskCreditLimit(
        SaveShipmentRequest request, CustomerRow customer, ShipmentHeader header, IReadOnlyList<ShipmentLine> lines,
        IReadOnlyDictionary<long, TaxCodeRate> rates, ShipmentSettings settings, IAmountRounder rounder)
    {
        if (request.ConfirmCreditLimit is not null || lines.Count == 0 || header.BillingFlow == Customer.BillThenShip
            || customer.Fields.CreditLimit is not > 0)
        {
            return;
        }
        var (_, totals) = ShipmentInvoice.Calculate(
            [.. lines.Select(l => new ShipmentInvoiceInput(l.Seq, l.ShipQty, l.ShipPrc, l.DcRate, l.TaxCodeId))], header.CurrencyCode,
            header.ExchangeRate, rates, settings, rounder);
        if (SalesOrderEntryRules.CreditLimitExceeded(customer.Fields.CreditLimit, customer.BalAmt, totals.TotBaseAmt, 0m))
        {
            throw new ConfirmationRequiredException(
                SalesOrderEntryRules.CreditLimitWarningCode, SalesOrderEntryRules.ConfirmCreditLimitField, customer.Fields.CreditLimit!.Value,
                customer.BalAmt + totals.TotBaseAmt);
        }
    }

    // The side effects of a save in the shipment's transaction: stock (SD-BR-163, SD-BR-176, MM-BR-601), lots (MM-BR-355), order
    // and invoice qty and order status (SD-BR-165, SD-BR-108, SD-BR-176, SD-BR-177), the SHIP_AND_BILL invoice (SD-BR-167) and the
    // shipment status (SD-BR-166)
    private async Task ApplyAsync(
        long id, string shipNo, ShipmentRow? stored, long? businessPlaceId, PreparedSave save, IReadOnlyDictionary<int, long> lineIds,
        string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var header = save.Header;
        await ReverseAsync(shipNo, stored, save.Deleted, header.ShipDt, user, transaction, cancellationToken);
        if (save.Added.Count > 0)
        {
            // Service items have no stock; the inventory service skips them and expands sets (MM-BR-007, MM-BR-008)
            var posted = await inventory.PostAsync(
                [.. save.Added.Select(l => new StockMovementRequest(
                    MovementType.Delivery, header.ShipDt, l.ItemId, l.WarehouseId, -l.ShipQty, null, shipNo, l.Seq, l.LotId, l.LotNo))],
                user, transaction, cancellationToken);
            await EnsureCostAsync(posted, save.ItemCodes, transaction, cancellationToken);
            var lots = posted.Where(m => m.LotId is not null && m.RefSeq is not null).GroupBy(m => m.RefSeq!.Value)
                .ToDictionary(g => g.Key, g => g.First().LotId!.Value);
            if (lots.Count > 0)
            {
                await shipments.SetLineLotsAsync(id, lots, transaction, cancellationToken);
            }
        }
        await TakeBackQtyAsync(header.BillingFlow, save.Added, save.Deleted, save.OrderLines, user, transaction, cancellationToken);

        var invoiceId = stored?.SalesInvoiceId;
        if (header.BillingFlow == Customer.ShipAndBill)
        {
            invoiceId = await BillAsync(invoiceId, businessPlaceId ?? 0, save, lineIds, user, transaction, cancellationToken);
        }
        var status = ShipmentStatus.FromBilling(
            save.Kept.Select(l => (l.ShipQty, l.BilledQty)).Concat(save.Added.Select(l => (l.ShipQty, l.BilledQty))));
        await shipments.SetStatusAsync(id, status, invoiceId, user, transaction, cancellationToken);
    }

    // MM-BR-601: a reversal is dated the day it is made, never before the shipment itself. Service lines have no movement
    private async Task ReverseAsync(
        string shipNo, ShipmentRow? stored, IReadOnlyList<StoredShipmentLine> lines, DateOnly shipDt, string user, IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (lines.Count == 0)
        {
            return;
        }
        // A migrated shipment's stock was issued under its invoice number (0303)
        string[] refKeys = stored?.SalesInvoiceNo is { } dnNo && stored.Id == stored.SalesInvoiceId ? [shipNo, dnNo] : [shipNo];
        var movementIds = await shipments.FindOpenMovementIdsAsync(refKeys, [.. lines.Select(l => l.Seq)], transaction, cancellationToken);
        if (movementIds.Count > 0)
        {
            var today = clock.Today();
            await inventory.ReverseAsync(movementIds, today < shipDt ? shipDt : today, user, transaction, cancellationToken);
        }
    }

    // SD-BR-163, SD-BR-114: no issue at a moving average cost of 0, set components included
    private async Task EnsureCostAsync(
        IReadOnlyList<GoodsMovement> posted, IReadOnlyDictionary<long, string> itemCodes, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var zero = posted.Where(m => m.Qty < 0 && m.Prc == 0).ToList();
        if (zero.Count == 0)
        {
            return;
        }
        var itemId = zero[0].ItemId;
        var code = itemCodes.GetValueOrDefault(itemId)
            ?? (await shipments.FindItemCodesAsync([itemId], transaction, cancellationToken)).GetValueOrDefault(itemId)
            ?? itemId.ToString(CultureInfo.InvariantCulture);
        ShipmentRules.EnsureCost(0m, code);
    }

    // Shipped qty of order lines (SD-BR-165, SD-BR-176), billed qty of SHIP_AND_BILL order lines (SD-BR-108), shipped qty of
    // BILL_THEN_SHIP invoice lines (SD-BR-176, SD-BR-177), then the status of the orders touched (SD-BR-012)
    private async Task TakeBackQtyAsync(
        string billingFlow, IReadOnlyList<ShipmentLine> added, IReadOnlyList<StoredShipmentLine> deleted,
        IReadOnlyDictionary<long, ShipmentOrderLine> orderLines, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var changes = added.Select(l => (l.SalesOrderLineId, l.SalesInvoiceLineId, Qty: l.ShipQty))
            .Concat(deleted.Select(l => (l.SalesOrderLineId, l.SalesInvoiceLineId, Qty: -l.ShipQty))).ToList();
        var shipQty = Sum(changes.Where(c => c.SalesOrderLineId is not null).Select(c => (c.SalesOrderLineId!.Value, c.Qty)));
        if (shipQty.Count > 0)
        {
            await shipments.AddOrderShipQtyAsync(shipQty, user, transaction, cancellationToken);
        }
        if (billingFlow == Customer.ShipAndBill)
        {
            var dnQty = Sum(changes.Where(c => c.SalesOrderLineId is not null && c.SalesInvoiceLineId is null).Select(c => (c.SalesOrderLineId!.Value, c.Qty)));
            if (dnQty.Count > 0)
            {
                await shipments.AddOrderDnQtyAsync(dnQty, user, transaction, cancellationToken);
            }
        }
        var shippedQty = Sum(changes.Where(c => c.SalesInvoiceLineId is not null).Select(c => (c.SalesInvoiceLineId!.Value, c.Qty)));
        if (shippedQty.Count > 0)
        {
            await shipments.AddInvoiceShippedQtyAsync(shippedQty, user, transaction, cancellationToken);
        }

        var orderIds = shipQty.Keys.Select(l => orderLines.GetValueOrDefault(l)?.SalesOrderId).OfType<long>().Distinct().ToList();
        if (orderIds.Count == 0)
        {
            return;
        }
        foreach (var order in (await shipments.FindOrderQuantitiesAsync(orderIds, transaction, cancellationToken)).GroupBy(q => q.SalesOrderId))
        {
            var status = SalesOrderStatus.FromShipments(order.Select(q => (q.SoQty, q.ShipQty)));
            if (status != order.First().Status)
            {
                await shipments.SetOrderStatusAsync(order.Key, status, user, transaction, cancellationToken);
            }
        }
    }

    // SD-BR-167: the shipment's invoice, made on the first save and kept in line with the shipment's lines afterwards
    private async Task<long> BillAsync(
        long? invoiceId, long businessPlaceId, PreparedSave save, IReadOnlyDictionary<int, long> lineIds, string user, IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        var h = save.Header;
        var terms = h.SalesOrderId is { } orderId ? await shipments.FindOrderTermsAsync(orderId, transaction, cancellationToken) : null;
        var termsCode = terms is null ? save.CustomerTerms : terms.Terms;
        var invoiceHeader = new ShipmentInvoiceHeader(
            h.CustomerId, h.SalesOrderId, h.ShipDt, terms?.DueDt ?? SalesOrderDefaults.DueDate(termsCode, h.ShipDt), termsCode, terms?.Rep, h.ShipVia,
            terms?.FreightCharge, h.ShipTo, h.CurrencyCode, h.ExchangeRate, h.WarehouseId, h.Remarks);
        if (invoiceId is null)
        {
            // BC-BR-321: a shipment's invoice belongs to the shipment's business place
            var dnNo = await numbers.NextAsync(DocumentNumberFormat.SalesInvoice, h.ShipDt, user, transaction, cancellationToken);
            invoiceId = await shipments.InsertInvoiceAsync(dnNo, businessPlaceId, invoiceHeader, user, transaction, cancellationToken);
        }
        // The invoice keeps the lines of the kept shipment lines and the direct lines the invoice screen added (SD-BR-136); new lines
        // continue its line numbers, and the whole invoice is recalculated so document tax rounding stays right (SD-BR-109)
        var stored = await shipments.FindInvoiceLineInputsAsync(invoiceId.Value, transaction, cancellationToken);
        var firstSeq = stored.Count == 0 ? 1 : stored.Max(l => l.Seq) + 1;
        var newLines = save.Added.Select((l, i) => (Line: l, Seq: firstSeq + i)).ToList();
        var inputs = stored.Concat(newLines.Select(n => new ShipmentInvoiceInput(n.Seq, n.Line.ShipQty, n.Line.ShipPrc, n.Line.DcRate, n.Line.TaxCodeId)))
            .OrderBy(l => l.Seq).ToList();
        var rates = save.Rates;
        if (inputs.Any(l => !rates.ContainsKey(l.TaxCodeId)))
        {
            rates = await TaxRatesAsync(inputs.Select(l => l.TaxCodeId).Concat(rates.Keys), h.ShipDt, transaction, cancellationToken);
        }
        var (amounts, totals) = ShipmentInvoice.Calculate(inputs, h.CurrencyCode, h.ExchangeRate, rates, save.Settings, save.Rounder);
        var bySeq = amounts.ToDictionary(a => a.Seq);
        var added = newLines.Select(n =>
        {
            var (l, a) = (n.Line, bySeq[n.Seq]);
            return new ShipmentInvoiceLineWrite(
                n.Seq, l.ItemId, l.ItemName, l.ShipQty, l.ShipPrc, a.DnAmt, l.SalesOrderLineId, l.DcCd, l.DcRate, l.WarehouseId, l.TaxCodeId,
                a.SupplyAmt, a.TaxAmt, lineIds[l.Seq]);
        }).ToList();
        await shipments.UpdateInvoiceAsync(
            invoiceId.Value, invoiceHeader, added, amounts, totals, user, transaction, cancellationToken);
        return invoiceId.Value;
    }

    private async Task<ShipmentDto> ReadAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var r = await shipments.FindAsync(id, forUpdate: false, transaction, cancellationToken) ?? throw new NotFoundException(NotFoundCode, id);
        var lines = await shipments.FindLinesAsync(id, transaction, cancellationToken);
        var s = r.ShipTo;
        return new ShipmentDto(
            r.Id, r.ShipNo, r.Status, r.BillingFlow, r.CustomerId, r.CustomerName, r.SalesOrderId, r.SoNo, r.ShipDt, r.WarehouseId, r.CurrencyCode,
            r.ExchangeRate, r.ShipVia, s.ShipName, s.ShipAddr1, s.ShipAddr2, s.ShipCity, s.ShipState, s.ShipZipCode, s.ShipCountry, s.ShipPhone,
            r.Remarks, r.SalesInvoiceId, r.SalesInvoiceNo,
            [.. lines.Select(l => new ShipmentLineDto(
                l.Id, l.Seq, l.ItemId, l.ItemCd, l.ItemName, l.Unit, l.WarehouseId, l.WarehouseCode, l.LotId, l.LotNo, l.ShipQty, l.ShipPrc, l.DcCd,
                l.DcRate, l.TaxCodeId, l.BilledQty, l.OnhandQty, l.SalesOrderLineId, l.SoNo, l.SoSeq, l.SalesInvoiceLineId, l.DnNo, l.DnSeq))],
            r.UpdatedAt, r.CreatedAt, r.CreatedBy, r.UpdatedBy);
    }

    private static Dictionary<long, decimal> Sum(IEnumerable<(long Key, decimal Qty)> changes) =>
        changes.GroupBy(c => c.Key).Select(g => (g.Key, Qty: g.Sum(c => c.Qty))).Where(x => x.Qty != 0).ToDictionary(x => x.Key, x => x.Qty);

    private static ShipmentShipTo ShipTo(SaveShipmentRequest r) => new(
        Clean(r.ShipName), Clean(r.ShipAddr1), Clean(r.ShipAddr2), Clean(r.ShipCity), Clean(r.ShipState)?.ToUpperInvariant(), Clean(r.ShipZipCode),
        Clean(r.ShipCountry), Clean(r.ShipPhone));

    private static ShipmentLineInput Input(SaveShipmentLineRequest line, long defaultWarehouseId, long defaultTaxCodeId) => new(
        line.Id, line.ItemId, Clean(line.ItemName), line.WarehouseId ?? defaultWarehouseId, line.ShipQty, line.ShipPrc ?? 0m,
        line.TaxCodeId ?? defaultTaxCodeId, line.LotId, Clean(line.LotNo)?.ToUpperInvariant(), line.SalesOrderLineId, line.SalesInvoiceLineId);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// A checked save: header, new lines, kept and deleted saved lines, the locked source lines, tax rates at the shipment date,
    /// amount settings, item codes and the customer's terms (for an invoice without an order).
    /// </summary>
    private sealed record PreparedSave(
        ShipmentHeader Header, IReadOnlyList<ShipmentLine> Added, IReadOnlyList<StoredShipmentLine> Kept, IReadOnlyList<StoredShipmentLine> Deleted,
        IReadOnlyDictionary<long, ShipmentOrderLine> OrderLines, IReadOnlyDictionary<long, ShipmentInvoiceLine> InvoiceLines,
        IReadOnlyDictionary<long, TaxCodeRate> Rates, ShipmentSettings Settings, IAmountRounder Rounder, IReadOnlyDictionary<long, string> ItemCodes,
        string? CustomerTerms);
}
