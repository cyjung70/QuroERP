using System.Data;
using QuroErp.Application.Bc.BusinessPlaces;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Bc.Tax;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Codes;
using QuroErp.Application.Common.DocumentNumbers;
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Sd.Customers;
using QuroErp.Contracts.Sd.SalesOrders;
using QuroErp.Domain.Bc.BusinessPlaces;
using QuroErp.Domain.Bc.Items;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Bc.Tax;
using QuroErp.Domain.Common;
using QuroErp.Domain.Common.Codes;
using QuroErp.Domain.Sd.Customers;
using QuroErp.Domain.Sd.SalesOrders;

namespace QuroErp.Application.Sd.SalesOrders;

/// <summary>
/// Sales orders (SD-UC-002): list, find, save, delete, line defaults and line files. Every write locks the header row and checks
/// updatedAt (409). Saves ask the ship-to (SD-BR-009), price level (SD-BR-010) and credit limit (SD-BR-309) questions. The cart
/// (SD-BR-011) and quotation import (SD-BR-657, SD-BR-658) come with their own tasks.
/// </summary>
public sealed class SalesOrderService(
    ISalesOrderRepository orders,
    ICustomerRepository customers,
    ITaxRepository taxes,
    ITaxRateProvider taxRates,
    ILocalizationRepository localization,
    IBusinessPlaceRepository businessPlaces,
    ExchangeRateService exchangeRates,
    DocumentNumberService numbers,
    ICodeValidator codes,
    IUnitOfWorkFactory unitOfWork)
{
    /// <summary>The sales order, customer or item does not exist. Arg: id or code.</summary>
    public const string NotFoundCode = "Common-E-006";

    public async Task<IReadOnlyList<SalesOrderSummaryDto>> SearchAsync(SalesOrderSearchQuery query, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var rows = await orders.SearchAsync(query, SalesOrderRoutes.MaxListSize, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return [.. rows.Select(r => new SalesOrderSummaryDto(r.Id, r.SoNo, r.Status, r.SoDt, r.CustomerId, r.CustomerName, r.CurrencyCode, r.TotSoAmt))];
    }

    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task<SalesOrderDto> GetAsync(long id, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var dto = await ReadAsync(id, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return dto;
    }

    /// <summary>Saves a new SO order with the next number of the order date's month (SD-BR-006).</summary>
    /// <exception cref="DomainException">SD-E-001, 002, 004, 006, 031, 035, 404; Common-E-002 for a code value not in use; Common-E-005, Common-E-006; BC-E-412, BC-E-423.</exception>
    /// <exception cref="ConfirmationRequiredException">SD-W-009, SD-W-010 or SD-W-309 when that question is not answered yet.</exception>
    /// <exception cref="BadRequestException">400 BC-E-321 when there is no default business place.</exception>
    public async Task<SalesOrderDto> CreateAsync(SaveSalesOrderRequest request, string user, CancellationToken cancellationToken)
    {
        await EnsureCodesAsync(request, cancellationToken);
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var write = await PrepareAsync(request, stored: null, storedLines: [], user, uow.Transaction, cancellationToken);
        // BC-BR-321: a new document belongs to the default business place; the screen does not choose it
        var businessPlaceId = await businessPlaces.FindDefaultIdAsync(uow.Transaction, cancellationToken)
            ?? throw new BadRequestException(BusinessPlaceRules.NotFoundCode);
        var soNo = await numbers.NextAsync(DocumentNumberFormat.SalesOrder, request.SoDt, user, uow.Transaction, cancellationToken);
        var id = await orders.InsertAsync(soNo, businessPlaceId, write, user, uow.Transaction, cancellationToken);
        var dto = await ReadAsync(id, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return dto;
    }

    /// <summary>Saves an SO order (SD-BR-003). The customer stays (SD-BR-023).</summary>
    /// <exception cref="DomainException">SD-E-003, SD-E-023 and the checks of <see cref="CreateAsync"/>.</exception>
    /// <exception cref="ConflictException">409 SD-E-035 when the billing flow changes after a shipment or invoice.</exception>
    /// <exception cref="ConcurrencyException">updatedAt is missing or differs from the stored row.</exception>
    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task<SalesOrderDto> UpdateAsync(long id, SaveSalesOrderRequest request, string user, CancellationToken cancellationToken)
    {
        await EnsureCodesAsync(request, cancellationToken);
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var row = await LockAsync(id, request.UpdatedAt ?? default, uow.Transaction, cancellationToken);
        SalesOrderStatus.EnsureEditable(row.Status, row.SoNo);
        SalesOrderEntryRules.EnsureSameCustomer(row.CustomerId, request.CustomerId);
        var lines = await orders.FindLinesAsync(id, uow.Transaction, cancellationToken);
        // A line already shipped or invoiced stays: deleting it would break the shipment or invoice line (SD-BR-003)
        var keep = request.Lines.Select(l => l.Id).OfType<long>().ToHashSet();
        if (lines.Any(l => !keep.Contains(l.Id) && (l.ShipQty != 0 || l.DnQty != 0)))
        {
            throw new DomainException(SalesOrderStatus.NotEditableCode, row.SoNo);
        }
        var write = await PrepareAsync(request, row, lines, user, uow.Transaction, cancellationToken);
        if (SalesOrderEntryRules.BillingFlowLocked(
                row.BillingFlow, write.Header.BillingFlow, await orders.HasShipmentOrInvoiceAsync(id, uow.Transaction, cancellationToken)))
        {
            throw new ConflictException(SalesOrderEntryRules.BillingFlowCode, write.Header.BillingFlow);
        }
        if (!await orders.UpdateAsync(id, write, row.UpdatedAt, user, uow.Transaction, cancellationToken))
        {
            throw new ConcurrencyException();
        }
        var dto = await ReadAsync(id, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return dto;
    }

    /// <exception cref="DomainException">SD-E-003 when the order is not SO or has a shipment or invoice.</exception>
    /// <exception cref="ConcurrencyException">updatedAt differs from the stored row.</exception>
    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task DeleteAsync(long id, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var row = await LockAsync(id, updatedAt, uow.Transaction, cancellationToken);
        SalesOrderStatus.EnsureEditable(row.Status, row.SoNo);
        if (await orders.HasShipmentOrInvoiceAsync(id, uow.Transaction, cancellationToken))
        {
            throw new DomainException(SalesOrderStatus.NotEditableCode, row.SoNo);
        }
        if (!await orders.DeleteAsync(id, updatedAt, uow.Transaction, cancellationToken))
        {
            throw new ConcurrencyException();
        }
        await uow.CommitAsync(cancellationToken);
    }

    /// <summary>The values of a new line for the item (SD-BR-016, 033, 401, 405).</summary>
    /// <exception cref="NotFoundException">Common-E-006 for an unknown customer or item code.</exception>
    /// <exception cref="DomainException">MM-E-809 when the item is not in use.</exception>
    public async Task<SalesOrderLineDefaultsDto> GetLineDefaultsAsync(SalesOrderLineDefaultsQuery query, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var context = await LineContextAsync(query.CustomerId, query.ShipState, query.SoDt, uow.Transaction, cancellationToken);
        var code = query.ItemCd.Trim().ToUpperInvariant();
        var found = await orders.FindItemsByKeyAsync([code], byBarcode: false, context.PriceLevel, uow.Transaction, cancellationToken);
        var item = found.Count > 0 ? found[0] : throw new NotFoundException(NotFoundCode, code);
        await uow.CommitAsync(cancellationToken);
        if (item.ValidFlag == Item.ValidFlagNo)
        {
            throw new DomainException(Item.NotInUseCode, item.ItemCd);
        }
        return context.Defaults(item, SalesOrderDefaults.Qty);
    }

    /// <summary>
    /// Reads a line or barcode file (SD-BR-017~022). All rows become lines with the file quantity and the customer's price, or
    /// none when any row is wrong. Nothing is saved.
    /// </summary>
    /// <exception cref="DomainException">SD-E-017 for a file over 1 MB or 1,000 lines; Common-E-002 for an unknown kind.</exception>
    /// <exception cref="NotFoundException">Common-E-006 for an unknown customer.</exception>
    public async Task<SalesOrderLineFileResult> ReadLineFileAsync(
        SalesOrderLineFileQuery query, string text, long byteLength, CancellationToken cancellationToken)
    {
        var byBarcode = query.Kind switch
        {
            SalesOrderLineFileKind.Item => false,
            SalesOrderLineFileKind.Barcode => true,
            _ => throw new DomainException("Common-E-002", "kind", query.Kind ?? string.Empty),
        };
        var (rows, errors) = SalesOrderLineFile.Parse(text, byteLength);
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var context = await LineContextAsync(query.CustomerId, query.ShipState, query.SoDt, uow.Transaction, cancellationToken);
        var keys = rows.Select(r => byBarcode ? r.Key : r.Key.ToUpperInvariant()).Distinct().ToList();
        var items = (await orders.FindItemsByKeyAsync(keys, byBarcode, context.PriceLevel, uow.Transaction, cancellationToken))
            .GroupBy(i => byBarcode ? i.UpcCode! : i.ItemCd).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        await uow.CommitAsync(cancellationToken);

        var lines = new List<SalesOrderLineDefaultsDto>(rows.Count);
        var all = errors.ToList();
        foreach (var row in rows)
        {
            if (!items.TryGetValue(byBarcode ? row.Key : row.Key.ToUpperInvariant(), out var item))
            {
                all.Add(new SalesOrderFileError(row.LineNo, row.Key, SalesOrderLineFile.NotFoundReason));
            }
            else if (item.ValidFlag == Item.ValidFlagNo)
            {
                all.Add(new SalesOrderFileError(row.LineNo, row.Key, SalesOrderLineFile.NotInUseReason));
            }
            else
            {
                lines.Add(context.Defaults(item, row.Qty));
            }
        }
        // SD-BR-019: one wrong row and nothing is added
        return all.Count > 0
            ? new SalesOrderLineFileResult([], [.. all.OrderBy(e => e.LineNo).Select(e => new SalesOrderLineFileErrorDto(e.LineNo, e.Value, e.Reason))])
            : new SalesOrderLineFileResult(lines, []);
    }

    // Code fields the request sends must be code values in use (codes.md); the customer's defaults were checked when it was saved
    private async Task EnsureCodesAsync(SaveSalesOrderRequest r, CancellationToken cancellationToken)
    {
        await codes.EnsureValidAsync(CodeMajors.SalesRep, Clean(r.Rep), cancellationToken);
        await codes.EnsureValidAsync(CodeMajors.Terms, Clean(r.Terms), cancellationToken);
        await codes.EnsureValidAsync(CodeMajors.ShipmentVia, Clean(r.ShipVia), cancellationToken);
        await codes.EnsureValidAsync(CodeMajors.FreightCharge, Clean(r.FreightCharge), cancellationToken);
        await codes.EnsureValidAsync(CodeMajors.State, Clean(r.ShipState)?.ToUpperInvariant(), cancellationToken);
    }

    private async Task<SalesOrderRow> LockAsync(long id, DateTimeOffset updatedAt, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var row = await orders.FindAsync(id, forUpdate: true, transaction, cancellationToken) ?? throw new NotFoundException(NotFoundCode, id);
        return row.UpdatedAt == updatedAt ? row : throw new ConcurrencyException();
    }

    // Applies the defaults, reads the masters, lets the Domain check and compute, and asks the save questions
    private async Task<SalesOrderWrite> PrepareAsync(
        SaveSalesOrderRequest request, SalesOrderRow? stored, IReadOnlyList<SalesOrderLineRow> storedLines, string user,
        IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var customer = await customers.FindAsync(request.CustomerId, forUpdate: false, transaction, cancellationToken)
            ?? throw new DomainException(SalesOrderEntryRules.NotFoundCode, request.CustomerId);
        var c = customer.Fields;
        var company = await localization.FindCompanyAsync(transaction, cancellationToken);
        var baseCurrency = company?.BaseCurrencyCode ?? CountryProfiles.Get(CountryProfiles.Us).BaseCurrencyCode;
        // BC-BR-424: amounts and tax use the SD module rounding mode; HALF_UP when the setting row is missing
        var sd = (await localization.FindDecimalSettingsAsync(transaction, cancellationToken)).FirstOrDefault(d => d.Module == DecimalModule.Sd);
        var settings = new SalesOrderSettings(
            baseCurrency, company?.TaxCalcMode ?? TaxCalcMode.Document, sd is null ? RoundingMode.HalfUp : Rounding.ParseMode(sd.RoundingMode));

        var currency = Clean(request.CurrencyCode)?.ToUpperInvariant() ?? stored?.CurrencyCode ?? Clean(c.CurrencyCode) ?? baseCurrency;
        var rate = request.ExchangeRate
            ?? (stored is not null && stored.CurrencyCode == currency ? stored.ExchangeRate : (decimal?)null)
            ?? (currency == baseCurrency ? 1m : await exchangeRates.DefaultRateAsync(currency, request.SoDt, transaction, cancellationToken))
            ?? throw new DomainException(SalesOrderEntryRules.CurrencyCode, currency, "0");
        var terms = Clean(request.Terms) ?? (stored is null ? Clean(c.Terms) : null);
        var header = new SalesOrderHeader(
            request.CustomerId, request.SoDt, request.ExpShipDt ?? request.SoDt, request.DueDt ?? SalesOrderDefaults.DueDate(terms, request.SoDt),
            currency, rate, Clean(request.BillingFlow) ?? stored?.BillingFlow ?? Clean(c.BillingFlow) ?? Customer.ShipAndBill,
            Clean(request.Rep) ?? (stored is null ? Clean(c.Rep) : null), terms, Clean(request.ShipVia), ShipTo(request, stored is null ? c : null),
            Clean(request.Remarks));

        var context = await LineContextAsync(customer, header.ShipTo.ShipState, request.SoDt, company?.CountryCode, transaction, cancellationToken);
        var items = (await orders.FindItemsByIdAsync([.. request.Lines.Select(l => l.ItemId).Distinct()], context.PriceLevel, transaction, cancellationToken))
            .ToDictionary(i => i.Id);
        var lines = request.Lines.Select(l =>
        {
            var item = items.GetValueOrDefault(l.ItemId);
            var dcCd = l.DcCd is null ? (l.Id is null ? context.DiscountCode : null) : Clean(l.DcCd);
            return new SalesOrderLineInput(
                l.Id, l.ItemId, Clean(l.ItemName), l.SoQty ?? SalesOrderDefaults.Qty,
                l.SoPrc ?? SalesOrderDefaults.UnitPrice(item?.LevelPrc, item?.StdPrc), dcCd,
                l.TaxCodeId ?? (item is null ? context.CustomerTaxCodeId : context.TaxDefault(item).TaxCodeId) ?? 0);
        }).ToList();

        var currencies = (await localization.FindCurrenciesAsync(transaction, cancellationToken)).Where(x => x.UseYn);
        var rates = new Dictionary<long, TaxCodeRate>();
        foreach (var code in (await taxes.FindTaxCodesAsync([.. lines.Select(l => l.TaxCodeId).Distinct()], transaction, cancellationToken)).Where(x => x.UseYn))
        {
            rates[code.Id] = await taxRates.GetRateAsync(code, request.SoDt, transaction, cancellationToken);
        }
        var rounder = new AmountRounder(currencies.Select(x => new Currency(x.CurrencyCode, x.Decimals)));
        var masters = new SalesOrderMasters(items.ToDictionary(i => i.Key, i => i.Value.ItemType ?? string.Empty), context.DiscountRates, rates);
        var (computed, totals) = SalesOrderEntryRules.Calculate(
            header, lines, storedLines.Count > 0 ? stored?.CurrencyCode : null, masters, settings, rounder);

        await AskAsync(request, customer, header, computed, totals, stored, storedLines, items, baseCurrency, user, transaction, cancellationToken);
        return new SalesOrderWrite(header, totals, computed);
    }

    // SD-BR-009, SD-BR-010, SD-BR-309: an unanswered question → 422 with its confirmField and nothing saved; yes to the first two
    // also updates the customer or the price level in this transaction
    private async Task AskAsync(
        SaveSalesOrderRequest request, CustomerRow customer, SalesOrderHeader header, IReadOnlyList<SalesOrderLine> lines, SalesOrderTotals totals,
        SalesOrderRow? stored, IReadOnlyList<SalesOrderLineRow> storedLines, IReadOnlyDictionary<long, SalesOrderItemRow> items, string baseCurrency,
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

        var priceLevel = Clean(c.PriceLevel);
        if (request.UpdatePriceLevel != false && priceLevel is not null)
        {
            var saved = storedLines.ToDictionary(
                l => l.Id,
                l => new SalesOrderLine(l.Id, l.Seq, l.ItemId, l.ItemName, l.SoQty, l.SoPrc, l.DcCd, l.DcRate, l.SoAmt, l.TaxCodeId, l.SupplyAmt, l.TaxAmt));
            var references = items.Values.Where(i => i.ItemType is not null)
                .ToDictionary(i => i.Id, i => new PriceLevelReference(i.Id, i.ItemCd, i.ItemType!, i.StdPrc, i.LevelPrc));
            var changes = SalesOrderEntryRules.PriceLevelChanges(lines, saved, priceLevel, header.CurrencyCode, baseCurrency, references);
            if (changes.Count > 0)
            {
                if (request.UpdatePriceLevel is null)
                {
                    throw new ConfirmationRequiredException(
                        SalesOrderEntryRules.PriceLevelWarningCode, SalesOrderEntryRules.UpdatePriceLevelField,
                        string.Join(", ", changes.Select(x => x.ItemCd)));
                }
                await orders.UpsertPriceLevelAsync(priceLevel, changes, user, transaction, cancellationToken);
            }
        }

        if (request.ConfirmCreditLimit is null
            && SalesOrderEntryRules.CreditLimitExceeded(c.CreditLimit, customer.BalAmt, totals.TotBaseAmt, stored?.TotBaseAmt ?? 0m))
        {
            throw new ConfirmationRequiredException(
                SalesOrderEntryRules.CreditLimitWarningCode, SalesOrderEntryRules.ConfirmCreditLimitField, c.CreditLimit!.Value,
                customer.BalAmt + totals.TotBaseAmt - (stored?.TotBaseAmt ?? 0m));
        }
    }

    // The request's ship-to; a new order whose ship-to is all empty takes the customer's (customer is null on update)
    private static SalesOrderShipTo ShipTo(SaveSalesOrderRequest r, CustomerFields? customer)
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

    private async Task<LineContext> LineContextAsync(
        long customerId, string? shipState, DateOnly? soDt, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var customer = await customers.FindAsync(customerId, forUpdate: false, transaction, cancellationToken)
            ?? throw new NotFoundException(NotFoundCode, customerId);
        var company = await localization.FindCompanyAsync(transaction, cancellationToken);
        return await LineContextAsync(
            customer, Clean(shipState)?.ToUpperInvariant() ?? Clean(customer.Fields.ShipState), soDt ?? DateOnly.FromDateTime(DateTime.Today),
            company?.CountryCode, transaction, cancellationToken);
    }

    // What line defaults need from the customer and masters (SD-BR-033, 401, 405, BC-BR-525, BC-BR-542)
    private async Task<LineContext> LineContextAsync(
        CustomerRow customer, string? shipState, DateOnly soDt, string? country, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var c = customer.Fields;
        var discountRates = await orders.FindDiscountRatesAsync(transaction, cancellationToken);
        var isKr = country == CountryProfiles.Kr;
        var exemptCodeId = await orders.FindTaxCodeIdOfKindAsync(isKr ? TaxKind.VatExempt : TaxKind.SalesTaxExempt, transaction, cancellationToken);
        TaxExemptionDefault? exemption = null;
        if (!isKr && shipState is not null)
        {
            var certificates = await taxes.FindExemptionsAsync(customer.Id, shipState, transaction, cancellationToken);
            exemption = TaxExemptionDefault.For(certificates.Select(x => new TaxExemptionCertificate(x.CertificateNo, x.StateCode, x.ValidTo)), shipState, soDt);
        }
        return new LineContext(
            Clean(c.PriceLevel), discountRates, SalesOrderDefaults.DiscountCode(c.DcStatus, discountRates), c.TaxCodeId, exemptCodeId, exemption, isKr);
    }

    private async Task<SalesOrderDto> ReadAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var r = await orders.FindAsync(id, forUpdate: false, transaction, cancellationToken) ?? throw new NotFoundException(NotFoundCode, id);
        var lines = await orders.FindLinesAsync(id, transaction, cancellationToken);
        var locked = await orders.HasShipmentOrInvoiceAsync(id, transaction, cancellationToken);
        var s = r.ShipTo;
        return new SalesOrderDto(
            r.Id, r.SoNo, r.Status, r.CustomerId, r.CustomerName, r.SoDt, r.ExpShipDt, r.DueDt, r.CurrencyCode, r.ExchangeRate, r.BillingFlow, locked,
            r.Rep, r.Terms, r.ShipVia, s.FreightCharge, s.ShipName, s.ShipAddr1, s.ShipAddr2, s.ShipCity, s.ShipState, s.ShipZipCode, s.ShipCountry,
            s.ShipPhone, s.ShipFax, r.Remarks, r.DcStatus, r.SupplyAmt, r.TaxAmt, r.TotSoAmt, r.TotBaseAmt,
            [.. lines.Select(l => new SalesOrderLineDto(
                l.Id, l.Seq, l.ItemId, l.ItemCd, l.ItemName, l.SoQty, l.SoPrc, l.SoAmt, l.DcCd, l.DcRate, l.StdPrc, l.TaxCodeId, l.SupplyAmt,
                l.TaxAmt, l.ShipQty, l.DnQty, l.InvNo, l.Unit))],
            r.UpdatedAt, r.CreatedAt, r.CreatedBy, r.UpdatedBy);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record LineContext(
        string? PriceLevel, IReadOnlyDictionary<string, decimal> DiscountRates, string? DiscountCode, long? CustomerTaxCodeId,
        long? ExemptTaxCodeId, TaxExemptionDefault? Exemption, bool IsKr)
    {
        public LineTaxDefault TaxDefault(SalesOrderItemRow item) => SalesOrderDefaults.TaxCode(
            CustomerTaxCodeId, ExemptTaxCodeId, IsKr ? item.VatKind != "EXEMPT" : item.Taxable, Exemption);

        public SalesOrderLineDefaultsDto Defaults(SalesOrderItemRow item, decimal qty)
        {
            var tax = TaxDefault(item);
            return new SalesOrderLineDefaultsDto(
                item.Id, item.ItemCd, item.ItemName, item.Unit, qty, SalesOrderDefaults.UnitPrice(item.LevelPrc, item.StdPrc), item.StdPrc,
                DiscountCode, DiscountCode is { } code ? DiscountRates[code] : 0m, tax.TaxCodeId, tax.Warning);
        }
    }
}
