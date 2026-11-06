using System.Data;
using QuroErp.Application.Bc.BusinessPlaces;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Bc.Tax;
using QuroErp.Application.Common;
using QuroErp.Application.Common.DocumentNumbers;
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Mm.Inventory;
using QuroErp.Contracts.Mm.SupplierInvoices;
using QuroErp.Domain.Bc.BusinessPlaces;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Bc.Tax;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.GoodsReceipts;
using QuroErp.Domain.Mm.Inventory;
using QuroErp.Domain.Mm.SupplierInvoices;

namespace QuroErp.Application.Mm.SupplierInvoices;

/// <summary>
/// Supplier invoices (MM-UC-032): list, find, save, delete and the receipt lines to import. One save is one transaction: header,
/// lines, number, the receipt lines' invoiced qty and the price variance movements (supplier-invoice.md "기본 흐름" 5). Every
/// write locks the header row and checks updatedAt (409). Payments clear the invoice balance (MM-BR-951).
/// </summary>
public sealed class SupplierInvoiceService(
    ISupplierInvoiceRepository invoices,
    ITaxRepository taxes,
    ITaxRateProvider taxRates,
    ILocalizationRepository localization,
    IExchangeRateRepository exchangeRates,
    IBusinessPlaceRepository businessPlaces,
    InventoryService inventory,
    DocumentNumberService numbers,
    IBusinessClock clock,
    IUnitOfWorkFactory unitOfWork)
{
    /// <summary>The supplier invoice does not exist. Arg: id.</summary>
    public const string NotFoundCode = "Common-E-006";

    public async Task<IReadOnlyList<SupplierInvoiceSummaryDto>> SearchAsync(SupplierInvoiceSearchQuery query, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var rows = await invoices.SearchAsync(query, SupplierInvoiceRoutes.MaxListSize, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return rows;
    }

    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task<SupplierInvoiceDto> GetAsync(long id, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var dto = await ReadAsync(id, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return dto;
    }

    /// <summary>
    /// MM-BR-763: receipt lines of the vendor the invoice can import. PoPrc is the PO line's net price the invoice price is
    /// compared with (MM-BR-754).
    /// </summary>
    public async Task<IReadOnlyList<OpenReceiptLineDto>> OpenReceiptLinesAsync(
        long vendorId, string? currencyCode, long? excludeInvoiceId, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var rows = await invoices.FindOpenReceiptLinesAsync(
            vendorId, Clean(currencyCode)?.ToUpperInvariant(), excludeInvoiceId, uow.Transaction, cancellationToken);
        var context = rows.Any(r => r.PoPrc is not null) ? await ContextAsync(uow.Transaction, cancellationToken) : null;
        await uow.CommitAsync(cancellationToken);
        return [.. rows.Select(r => new OpenReceiptLineDto(
            r.GoodsReceiptLineId, r.GoodsReceiptId, r.GrNo, r.GrSeq, r.GrDt, r.ItemId, r.ItemCd, r.ItemName, r.Unit, r.GrQty, r.ReturnedQty,
            r.InvoicedQty, r.OpenQty, r.GrPrc,
            r.PoPrc is null || context is null ? null : SupplierInvoiceRules.ReferencePrice(Receipt(r), r.CurrencyCode, context.Decimals),
            r.TaxCodeId, r.TaxCode, r.Deductible, r.CurrencyCode, r.ExchangeRate))];
    }

    /// <summary>Saves a new invoice with the next VI number of the invoice date's month (MM-BR-752) and posts its price variances.</summary>
    /// <exception cref="DomainException">
    /// MM-E-751, 753, 754, 764; the inventory checks of a PV (MM-E-002 is never raised: the PV moves out of a closed month);
    /// Common-E-005, Common-E-006; BC-E-412, BC-E-423; SD-E-907.
    /// </exception>
    /// <exception cref="ConfirmationRequiredException">MM-W-754 until confirmPriceDifference is true (MM-BR-754).</exception>
    /// <exception cref="BadRequestException">400 BC-E-321 when there is no default business place.</exception>
    public async Task<SupplierInvoiceDto> CreateAsync(SaveSupplierInvoiceRequest request, string user, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var transaction = uow.Transaction;
        var save = await PrepareAsync(request, stored: null, storedLines: [], transaction, cancellationToken);
        await EnsureUniqueRefNoAsync(save.Write.Header, null, transaction, cancellationToken);
        // BC-BR-321: a new document belongs to the default business place; the screen does not choose it
        var businessPlaceId = await businessPlaces.FindDefaultIdAsync(transaction, cancellationToken)
            ?? throw new BadRequestException(BusinessPlaceRules.NotFoundCode);
        var invoiceNo = await numbers.NextAsync(DocumentNumberFormat.SupplierInvoice, request.InvoiceDt, user, transaction, cancellationToken);
        var id = await invoices.InsertAsync(invoiceNo, businessPlaceId, save.Write, user, transaction, cancellationToken);
        await ApplyAsync(invoiceNo, save, user, transaction, cancellationToken);
        var dto = await ReadAsync(id, transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return dto;
    }

    /// <summary>
    /// Changes the header, adds lines and deletes the lines left out (MM-BR-760). A deleted line gives back its invoiced qty and
    /// reverses its price variance.
    /// </summary>
    /// <exception cref="DomainException">
    /// MM-E-760 (also a new date, currency or rate while lines are saved), 762 and the checks of <see cref="CreateAsync"/>.
    /// </exception>
    /// <exception cref="ConfirmationRequiredException">MM-W-754 as on <see cref="CreateAsync"/>.</exception>
    /// <exception cref="ConcurrencyException">updatedAt is missing or differs, or a line id is not one of this invoice.</exception>
    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task<SupplierInvoiceDto> UpdateAsync(long id, SaveSupplierInvoiceRequest request, string user, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var transaction = uow.Transaction;
        var row = await LockAsync(id, request.UpdatedAt ?? default, transaction, cancellationToken);
        SupplierInvoiceRules.EnsureEditable(row.Type, row.InvoiceNo, row.ClsAmt);
        var lines = await invoices.FindLinesAsync(id, transaction, cancellationToken);
        var save = await PrepareAsync(request, row, lines, transaction, cancellationToken);
        await EnsureUniqueRefNoAsync(save.Write.Header, id, transaction, cancellationToken);
        if (!await invoices.UpdateAsync(
                id, save.Write, [.. save.Deleted.Select(l => l.Id)], row.UpdatedAt, user, transaction, cancellationToken))
        {
            throw new ConcurrencyException();
        }
        await ApplyAsync(row.InvoiceNo, save, user, transaction, cancellationToken);
        var dto = await ReadAsync(id, transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return dto;
    }

    /// <summary>Deletes the invoice; every line gives back its invoiced qty and reverses its price variance (MM-BR-757, MM-BR-760).</summary>
    /// <exception cref="DomainException">MM-E-760, 762.</exception>
    /// <exception cref="ConcurrencyException">updatedAt differs from the stored row.</exception>
    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task DeleteAsync(long id, DateTimeOffset updatedAt, string user, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var transaction = uow.Transaction;
        var row = await LockAsync(id, updatedAt, transaction, cancellationToken);
        SupplierInvoiceRules.EnsureEditable(row.Type, row.InvoiceNo, row.ClsAmt);
        var lines = await invoices.FindLinesAsync(id, transaction, cancellationToken);
        // Lock the receipts before giving their qty back, as a save does
        await invoices.LockReceiptLinesAsync(
            [.. lines.Select(l => l.GoodsReceiptLineId).OfType<long>().Distinct()], transaction, cancellationToken);
        var save = new PreparedSave(new SupplierInvoiceWrite(Header(row), new(0, 0, 0, 0), 0, SupplierInvoiceStatus.Void, []), [], lines, new Dictionary<long, InvoiceReceiptLine>());
        await ApplyAsync(row.InvoiceNo, save, user, transaction, cancellationToken);
        if (!await invoices.DeleteAsync(id, updatedAt, transaction, cancellationToken))
        {
            throw new ConcurrencyException();
        }
        await uow.CommitAsync(cancellationToken);
    }

    private async Task<SupplierInvoiceRow> LockAsync(long id, DateTimeOffset updatedAt, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var row = await invoices.FindAsync(id, forUpdate: true, transaction, cancellationToken) ?? throw new NotFoundException(NotFoundCode, id);
        return row.UpdatedAt == updatedAt ? row : throw new ConcurrencyException();
    }

    // MM-BR-764: one vendor invoice number per vendor among invoices that are not void
    private async Task EnsureUniqueRefNoAsync(SupplierInvoiceHeader header, long? exceptId, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        if (await invoices.RefNoExistsAsync(header.VendorId, header.RefNo!, exceptId, transaction, cancellationToken))
        {
            throw new DomainException(SupplierInvoiceRules.DuplicateRefNoCode, header.RefNo!);
        }
    }

    // Applies the defaults (MM-BR-754~756), locks and reads the receipt lines, lets the Domain check and compute, and asks the
    // price difference question (MM-BR-754)
    private async Task<PreparedSave> PrepareAsync(
        SaveSupplierInvoiceRequest request, SupplierInvoiceRow? stored, IReadOnlyList<SupplierInvoiceLineRow> storedLines,
        IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var vendor = await invoices.FindVendorAsync(request.VendorId, transaction, cancellationToken)
            ?? throw new DomainException(SupplierInvoiceRules.NotFoundCode, request.VendorId);
        var context = await ContextAsync(transaction, cancellationToken);
        var invoiceNo = stored?.InvoiceNo ?? "";

        // MM-BR-760: saved lines come back unchanged with their id; the ones left out are deleted
        var storedById = storedLines.ToDictionary(l => l.Id);
        var kept = new List<StoredSupplierInvoiceLine>();
        foreach (var line in request.Lines.Where(l => l.Id is not null))
        {
            // A line id of another invoice means the screen is stale
            var saved = (storedById.GetValueOrDefault(line.Id!.Value) ?? throw new ConcurrencyException()).ToStored();
            SupplierInvoiceRules.EnsureUnchanged(invoiceNo, saved, Input(line, saved.Prc, saved.TaxCodeId, saved.Deductible));
            kept.Add(saved);
        }
        var keptIds = kept.Select(l => l.Id).ToHashSet();
        var deleted = storedLines.Where(l => !keptIds.Contains(l.Id)).ToList();
        var addedRequests = request.Lines.Where(l => l.Id is null).ToList();
        if (stored is not null && kept.Count > 0 && request.VendorId != stored.VendorId)
        {
            throw new DomainException(SupplierInvoiceRules.ReceiptMismatchCode, stored.CurrencyCode);
        }

        // Lock the receipts before reading their invoiced qty, so two invoices of one receipt line see each other's (MM-BR-753)
        var receiptLines = (await invoices.LockReceiptLinesAsync(
                [.. addedRequests.Select(l => l.GoodsReceiptLineId).Concat(deleted.Select(l => l.GoodsReceiptLineId).OfType<long>()).Distinct()],
                transaction, cancellationToken))
            .ToDictionary(l => l.Id);
        var added = addedRequests.Select(l => receiptLines.GetValueOrDefault(l.GoodsReceiptLineId) is { } r
                ? Input(l, r.GrPrc, r.TaxCodeId, r.Deductible)
                : throw new DomainException(SupplierInvoiceRules.NotFoundCode, l.GoodsReceiptLineId))
            .ToList();
        // MM-BR-760: an invoice with saved lines keeps its currency and rate
        var hasSavedLines = storedLines.Count > 0;
        var currencyCode = SupplierInvoiceRules.DocumentCurrency(
            hasSavedLines ? stored!.CurrencyCode : null, added.Select(l => receiptLines[l.GoodsReceiptLineId]));
        var exchangeRate = request.ExchangeRate
            ?? (hasSavedLines ? stored!.ExchangeRate : await DefaultRateAsync(currencyCode, request.InvoiceDt, context.Settings, transaction, cancellationToken));
        var header = new SupplierInvoiceHeader(
            request.VendorId, Clean(request.RefNo)?.ToUpperInvariant(), request.InvoiceDt, request.DueDt ?? request.InvoiceDt,
            Clean(request.Terms) ?? (stored is null ? vendor.Terms : null), currencyCode, exchangeRate, request.TaxInvoiceReceived,
            Clean(request.Remarks));
        if (stored is not null)
        {
            SupplierInvoiceRules.EnsureHeaderChange(invoiceNo, Header(stored), storedLines.Count, header);
        }

        var masters = new SupplierInvoiceMasters(
            receiptLines,
            await TaxCodesAsync(kept.Select(l => l.TaxCodeId).Concat(added.Select(l => l.TaxCodeId)), header.InvoiceDt, transaction, cancellationToken));
        var released = deleted.Where(l => l.GoodsReceiptLineId is not null)
            .GroupBy(l => l.GoodsReceiptLineId!.Value).ToDictionary(g => g.Key, g => g.Sum(l => l.Qty));
        var nextSeq = storedLines.Count == 0 ? 1 : storedLines.Max(l => l.Seq) + 1;
        var (lines, totals) = SupplierInvoiceRules.Calculate(
            header, kept, added, nextSeq, released, masters, context.Settings, context.Rounder, context.Decimals);

        // MM-BR-754: a price that differs from the PO price is saved once the user confirms
        var differences = SupplierInvoiceRules.PriceDifferences(lines, receiptLines, currencyCode, context.Decimals);
        if (differences.Count > 0 && request.ConfirmPriceDifference != true)
        {
            var first = differences[0];
            throw new ConfirmationRequiredException(
                SupplierInvoiceRules.PriceDifferenceWarningCode, SupplierInvoiceRules.ConfirmPriceDifferenceField, first.Seq, first.Prc,
                first.ReferencePrice);
        }

        var clsAmt = stored?.ClsAmt ?? 0m;
        var balAmt = SupplierInvoiceRules.BalanceOf(totals.TotAmt, clsAmt);
        var write = new SupplierInvoiceWrite(header, totals, balAmt, SupplierInvoiceRules.StatusOf(false, totals.TotAmt, balAmt), lines);
        return new PreparedSave(write, [.. lines.Where(l => l.Id is null)], deleted, receiptLines);
    }

    // BC-BR-412: the base currency is 1, otherwise the latest rate on or before the invoice date
    private async Task<decimal> DefaultRateAsync(
        string currencyCode, DateOnly invoiceDt, SupplierInvoiceSettings settings, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var latest = currencyCode == settings.BaseCurrencyCode
            ? null
            : await exchangeRates.FindLatestAsync(currencyCode, invoiceDt, transaction, cancellationToken);
        return ExchangeRateRules.DefaultRate(currencyCode, settings.BaseCurrencyCode, latest)
            ?? throw new DomainException(
                ExchangeRateRules.InvalidCode, invoiceDt.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), currencyCode);
    }

    private async Task<IReadOnlyDictionary<long, ReceiptTaxCode>> TaxCodesAsync(
        IEnumerable<long> taxCodeIds, DateOnly invoiceDt, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var codes = await taxes.FindTaxCodesAsync([.. taxCodeIds.Distinct()], transaction, cancellationToken);
        var result = new Dictionary<long, ReceiptTaxCode>();
        foreach (var code in codes.Where(c => c.UseYn))
        {
            result[code.Id] = new ReceiptTaxCode(await taxRates.GetRateAsync(code, invoiceDt, transaction, cancellationToken), code.TaxKind);
        }
        return result;
    }

    // Company settings for amounts (BC-BR-424, BC-BR-543) and the unit price decimals of the document currency's country
    // (BC-BR-425); without a company the base currency is USD
    private async Task<AmountContext> ContextAsync(IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var company = await localization.FindCompanyAsync(transaction, cancellationToken);
        var currencies = await localization.FindCurrenciesAsync(transaction, cancellationToken);
        var decimalSettings = await localization.FindDecimalSettingsAsync(transaction, cancellationToken);
        var countryDecimals = await localization.FindCountryDecimalsAsync(transaction, cancellationToken);
        var baseCurrency = company?.BaseCurrencyCode ?? CountryProfiles.Get(CountryProfiles.Us).BaseCurrencyCode;
        var mm = decimalSettings.FirstOrDefault(d => d.Module == DecimalModule.Mm);
        var settings = new SupplierInvoiceSettings(
            baseCurrency, company?.TaxCalcMode ?? TaxCalcMode.Document, mm is null ? RoundingMode.HalfUp : Rounding.ParseMode(mm.RoundingMode));
        var decimals = new DecimalPolicy(
            company?.CountryCode ?? CountryProfiles.Us,
            decimalSettings.Select(s => new DecimalSetting(s.Module, s.QtyDecimals, Rounding.ParseMode(s.RoundingMode))),
            countryDecimals.Select(d => new CountryDecimal(d.CountryCode, d.Module, d.UnitPriceDecimals)),
            currencies.ToDictionary(c => c.CurrencyCode, c => c.CountryCode));
        var rounder = new AmountRounder(currencies.Where(c => c.UseYn).Select(c => new Currency(c.CurrencyCode, c.Decimals)));
        return new AmountContext(settings, rounder, decimals);
    }

    // The side effects of a save in the invoice's transaction: price variances (MM-BR-758~760, MM-BR-601) and the receipt lines'
    // invoiced qty (MM-BR-757). The receipt status is computed from that qty when it is read (MM-BR-105)
    private async Task ApplyAsync(string invoiceNo, PreparedSave save, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var header = save.Write.Header;
        var movementIds = save.Deleted.Count == 0
            ? []
            : await invoices.FindOpenMovementIdsAsync(invoiceNo, [.. save.Deleted.Select(l => l.Seq)], transaction, cancellationToken);
        if (movementIds.Count > 0)
        {
            // MM-BR-601: the reversal is dated the day it is made, never before the invoice; a closed month moves it on (MM-BR-759)
            var today = clock.Today();
            await inventory.ReverseAsync(movementIds, today < header.InvoiceDt ? header.InvoiceDt : today, user, transaction, cancellationToken);
        }
        var variances = save.Added.Where(l => l.PvAmt != 0).ToList();
        if (variances.Count > 0)
        {
            // MM-BR-758, MM-BR-759: qty 0 at the receipt line's item, warehouse and lot, dated the invoice date
            await inventory.PostAsync(
                [.. variances.Select(l =>
                {
                    var receipt = save.ReceiptLines[l.GoodsReceiptLineId];
                    return new StockMovementRequest(
                        MovementType.PriceVariance, header.InvoiceDt, receipt.ItemId, receipt.WarehouseId, 0m, 0m, invoiceNo, l.Seq,
                        receipt.LotId, Amount: l.PvAmt);
                })],
                user, transaction, cancellationToken);
        }

        var qtyByReceiptLine = save.Added.Select(l => (Line: l.GoodsReceiptLineId, l.Qty))
            .Concat(save.Deleted.Where(l => l.GoodsReceiptLineId is not null).Select(l => (Line: l.GoodsReceiptLineId!.Value, Qty: -l.Qty)))
            .GroupBy(x => x.Line).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));
        if (qtyByReceiptLine.Count > 0)
        {
            await invoices.AddInvoicedQtyAsync(qtyByReceiptLine, user, transaction, cancellationToken);
        }
    }

    private async Task<SupplierInvoiceDto> ReadAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var r = await invoices.FindAsync(id, forUpdate: false, transaction, cancellationToken) ?? throw new NotFoundException(NotFoundCode, id);
        var lines = await invoices.FindLinesAsync(id, transaction, cancellationToken);
        return new SupplierInvoiceDto(
            r.Id, r.InvoiceNo, r.Type, r.Status, r.VendorId, r.VendorName, r.RefNo, r.InvoiceDt, r.DueDt, r.Terms, r.CurrencyCode,
            r.ExchangeRate, r.TaxInvoiceReceived, r.Remarks, r.SupplyAmt, r.TaxAmt, r.TotAmt, r.TotBaseAmt, r.ClsAmt, r.BalAmt,
            r.VendorReturnId,
            [.. lines.Select(l => new SupplierInvoiceLineDto(
                l.Id, l.Seq, l.GoodsReceiptLineId, l.GrNo, l.GrSeq, l.ItemId, l.ItemCd, l.ItemName, l.Unit, l.GrQty, l.GrPrc, l.Qty, l.Prc,
                l.TaxCodeId, l.TaxCode, l.SupplyAmt, l.TaxAmt, l.Deductible, l.PvAmt))],
            r.UpdatedAt, r.CreatedAt, r.CreatedBy, r.UpdatedBy);
    }

    private static SupplierInvoiceHeader Header(SupplierInvoiceRow row) =>
        new(row.VendorId, row.RefNo, row.InvoiceDt, row.DueDt, row.Terms, row.CurrencyCode, row.ExchangeRate, row.TaxInvoiceReceived, row.Remarks);

    // Only what the reference price needs from an open receipt line (MM-BR-754)
    private static InvoiceReceiptLine Receipt(OpenReceiptLineRow r) => new(
        r.GoodsReceiptLineId, r.GrNo, r.GrSeq, 0, r.CurrencyCode, r.ExchangeRate, r.ItemId, null, 0, null, r.GrQty, r.GrPrc, r.TaxCodeId, "",
        0m, 0m, r.Deductible, r.InvoicedQty, r.ReturnedQty, r.PoPrc, r.PoDcRate);

    private static SupplierInvoiceLineInput Input(SaveSupplierInvoiceLineRequest line, decimal defaultPrc, long defaultTaxCodeId, bool defaultDeductible) =>
        new(line.GoodsReceiptLineId, line.Qty, line.Prc ?? defaultPrc, line.TaxCodeId ?? defaultTaxCodeId, line.Deductible ?? defaultDeductible);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record AmountContext(SupplierInvoiceSettings Settings, IAmountRounder Rounder, IDecimalPolicy Decimals);

    /// <summary>A checked save: what to write, the new and deleted lines and the locked receipt lines.</summary>
    private sealed record PreparedSave(
        SupplierInvoiceWrite Write, IReadOnlyList<SupplierInvoiceLine> Added, IReadOnlyList<SupplierInvoiceLineRow> Deleted,
        IReadOnlyDictionary<long, InvoiceReceiptLine> ReceiptLines);
}
