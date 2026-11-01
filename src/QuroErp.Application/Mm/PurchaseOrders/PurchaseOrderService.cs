using System.Data;
using QuroErp.Application.Bc.BusinessPlaces;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Bc.Tax;
using QuroErp.Application.Common;
using QuroErp.Application.Common.DocumentNumbers;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Mm.PurchaseOrders;
using QuroErp.Domain.Bc.BusinessPlaces;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Bc.Tax;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.PurchaseOrders;

namespace QuroErp.Application.Mm.PurchaseOrders;

/// <summary>
/// Purchase orders (MM-UC-003): list, find, save, delete, approve, close and cancel. Every write locks the header row and
/// checks updatedAt (409). The vendor price question (MM-BR-016), request import (MM-BR-554) and printing come later.
/// </summary>
public sealed class PurchaseOrderService(
    IPurchaseOrderRepository orders,
    ITaxRepository taxes,
    ITaxRateProvider taxRates,
    ILocalizationRepository localization,
    IBusinessPlaceRepository businessPlaces,
    DocumentNumberService numbers,
    IUnitOfWorkFactory unitOfWork)
{
    /// <summary>The purchase order does not exist. Arg: id.</summary>
    public const string NotFoundCode = "Common-E-006";

    public async Task<IReadOnlyList<PurchaseOrderSummaryDto>> SearchAsync(PurchaseOrderSearchQuery query, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var rows = await orders.SearchAsync(query, PurchaseOrderRoutes.MaxListSize, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return rows.Select(r => new PurchaseOrderSummaryDto(r.Id, r.PoNo, r.Status, r.PoDt, r.VendorId, r.VendorName, r.CurrencyCode, r.TotPoAmt))
            .ToList();
    }

    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task<PurchaseOrderDto> GetAsync(long id, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var dto = await ReadAsync(id, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return dto;
    }

    /// <summary>Saves a new draft with the next PO number of the order date's month (MM-BR-017, MM-BR-019).</summary>
    /// <exception cref="DomainException">MM-E-011, 012, 014, 025, 026, 027, 031; Common-E-005, Common-E-006; BC-E-412, BC-E-513.</exception>
    /// <exception cref="BadRequestException">400 BC-E-321 when there is no default business place.</exception>
    public async Task<PurchaseOrderDto> CreateAsync(SavePurchaseOrderRequest request, string user, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var write = await PrepareAsync(request, previousCurrencyCode: null, PoStatus.Draft, revisionNo: 0, uow.Transaction, cancellationToken);
        // BC-BR-321: a new document belongs to the default business place; the screen does not choose it
        var businessPlaceId = await businessPlaces.FindDefaultIdAsync(uow.Transaction, cancellationToken)
            ?? throw new BadRequestException(BusinessPlaceRules.NotFoundCode);
        var poNo = await numbers.NextAsync(DocumentNumberFormat.PurchaseOrder, request.PoDt, user, uow.Transaction, cancellationToken);
        var id = await orders.InsertAsync(poNo, businessPlaceId, write, user, uow.Transaction, cancellationToken);
        var dto = await ReadAsync(id, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return dto;
    }

    /// <summary>Saves a DR or PO order with nothing received; a PO stays approved and counts a revision (MM-BR-013, MM-BR-021).</summary>
    /// <exception cref="DomainException">MM-E-013 and the checks of <see cref="CreateAsync"/>.</exception>
    /// <exception cref="ConcurrencyException">updatedAt is missing or differs from the stored row.</exception>
    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task<PurchaseOrderDto> UpdateAsync(long id, SavePurchaseOrderRequest request, string user, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var row = await LockAsync(id, request.UpdatedAt ?? default, uow.Transaction, cancellationToken);
        var lines = await orders.FindLinesAsync(id, uow.Transaction, cancellationToken);
        var po = ToDomain(row, lines, targetStatus: null);
        PoStatusRules.EnsureEditable(po);
        var write = await PrepareAsync(
            request, lines.Count > 0 ? row.CurrencyCode : null, po.Status, PoEntryRules.RevisionAfterSave(po.Status, row.RevisionNo),
            uow.Transaction, cancellationToken);
        if (!await orders.UpdateAsync(id, write, row.UpdatedAt, user, uow.Transaction, cancellationToken))
        {
            throw new ConcurrencyException();
        }
        var dto = await ReadAsync(id, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return dto;
    }

    /// <exception cref="DomainException">MM-E-013 when the order is not DR or PO, or something was received.</exception>
    /// <exception cref="ConcurrencyException">updatedAt differs from the stored row.</exception>
    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task DeleteAsync(long id, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var row = await LockAsync(id, updatedAt, uow.Transaction, cancellationToken);
        PoStatusRules.EnsureEditable(ToDomain(row, await orders.FindLinesAsync(id, uow.Transaction, cancellationToken), targetStatus: null));
        if (!await orders.DeleteAsync(id, updatedAt, uow.Transaction, cancellationToken))
        {
            throw new ConcurrencyException();
        }
        await uow.CommitAsync(cancellationToken);
    }

    /// <summary>DR → PO (MM-BR-020).</summary>
    /// <exception cref="DomainException">MM-E-020 for another status or an order without lines.</exception>
    public Task<PurchaseOrderDto> ApproveAsync(long id, DateTimeOffset updatedAt, string user, CancellationToken cancellationToken) =>
        ChangeStatusAsync(id, updatedAt, user, PoStatus.Approved, PoStatusRules.EnsureApprove, cancellationToken);

    /// <summary>PO or RE → MC (MM-BR-022).</summary>
    /// <exception cref="DomainException">MM-E-022 for another status.</exception>
    public Task<PurchaseOrderDto> CloseAsync(long id, DateTimeOffset updatedAt, string user, CancellationToken cancellationToken) =>
        ChangeStatusAsync(id, updatedAt, user, PoStatus.Closed, PoStatusRules.EnsureClose, cancellationToken);

    /// <summary>PO with nothing received → CN (MM-BR-023).</summary>
    /// <exception cref="DomainException">MM-E-023 for another status or when something was received.</exception>
    public Task<PurchaseOrderDto> CancelAsync(long id, DateTimeOffset updatedAt, string user, CancellationToken cancellationToken) =>
        ChangeStatusAsync(id, updatedAt, user, PoStatus.Cancelled, PoStatusRules.EnsureCancel, cancellationToken);

    private async Task<PurchaseOrderDto> ChangeStatusAsync(
        long id, DateTimeOffset updatedAt, string user, PoStatus target, Action<PurchaseOrder> ensure, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var row = await LockAsync(id, updatedAt, uow.Transaction, cancellationToken);
        var po = ToDomain(row, await orders.FindLinesAsync(id, uow.Transaction, cancellationToken), target.ToCode());
        ensure(po);
        PoStatusRules.EnsureTransition(po.Status, target);
        if (!await orders.SetStatusAsync(id, target, updatedAt, user, uow.Transaction, cancellationToken))
        {
            throw new ConcurrencyException();
        }
        var dto = await ReadAsync(id, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return dto;
    }

    private async Task<PurchaseOrderRow> LockAsync(long id, DateTimeOffset updatedAt, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var row = await orders.FindAsync(id, forUpdate: true, transaction, cancellationToken) ?? throw new NotFoundException(NotFoundCode, id);
        return row.UpdatedAt == updatedAt ? row : throw new ConcurrencyException();
    }

    // A status outside PO_STATUS cannot change (MM-BR-019): edit and delete answer MM-E-013, status actions MM-E-019
    private static PurchaseOrder ToDomain(PurchaseOrderRow row, IReadOnlyList<PurchaseOrderLineRow> lines, string? targetStatus)
    {
        if (!PoStatusCode.TryParse(row.Status, out var status))
        {
            throw targetStatus is null
                ? new DomainException(PoStatusRules.NotEditableCode, row.PoNo)
                : new DomainException(PoStatusRules.InvalidTransitionCode, row.Status, targetStatus);
        }
        return new PurchaseOrder(row.PoNo, status, [.. lines.Select(l => new PoLine(l.Seq, l.PoQty, l.PoPrc, l.DcRate, l.ReceivedQty))]);
    }

    // Applies the defaults (MM-BR-026, MM-BR-031, BC-BR-542), reads the masters and lets the Domain check and compute
    private async Task<PurchaseOrderWrite> PrepareAsync(
        SavePurchaseOrderRequest request, string? previousCurrencyCode, PoStatus status, int revisionNo, IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        var vendor = await orders.FindVendorAsync(request.VendorId, transaction, cancellationToken)
            ?? throw new DomainException(PoEntryRules.NotFoundCode, request.VendorId);
        var company = await localization.FindCompanyAsync(transaction, cancellationToken);
        var currencies = (await localization.FindCurrenciesAsync(transaction, cancellationToken)).Where(c => c.UseYn);
        var baseCurrency = company?.BaseCurrencyCode ?? CountryProfiles.Get(CountryProfiles.Us).BaseCurrencyCode;
        // BC-BR-424: amounts and tax use the MM module rounding mode; HALF_UP when the setting row is missing
        var mm = (await localization.FindDecimalSettingsAsync(transaction, cancellationToken)).FirstOrDefault(d => d.Module == DecimalModule.Mm);
        var settings = new PurchaseOrderSettings(
            baseCurrency, company?.TaxCalcMode ?? TaxCalcMode.Document, mm is null ? RoundingMode.HalfUp : Rounding.ParseMode(mm.RoundingMode));

        var header = new PurchaseOrderHeader(
            request.VendorId, request.PoDt, Clean(request.CurrencyCode) ?? vendor.CurrencyCode ?? baseCurrency, request.ExchangeRate ?? 1m,
            Clean(request.Incoterms)?.ToUpperInvariant(), request.DueDt, request.WarehouseId, Clean(request.PaymentTerms), Clean(request.Buyer),
            Clean(request.Remarks));
        var lines = request.Lines.Select(l => new PurchaseOrderLineInput(
            l.Id, l.ItemId, Clean(l.ItemName), l.PoQty, l.PoPrc, l.DcRate, l.DueDt ?? header.DueDt, l.WarehouseId ?? header.WarehouseId,
            l.TaxCodeId ?? vendor.TaxCodeId ?? 0)).ToList();

        var itemTypes = await orders.FindItemTypesAsync([.. lines.Select(l => l.ItemId).Distinct()], transaction, cancellationToken);
        var warehouseIds = lines.Select(l => l.WarehouseId).Append(header.WarehouseId).OfType<long>().Distinct().ToList();
        var warehouses = await orders.FindUsableWarehousesAsync(warehouseIds, transaction, cancellationToken);
        var taxCodes = await taxes.FindTaxCodesAsync([.. lines.Select(l => l.TaxCodeId).Distinct()], transaction, cancellationToken);
        var rates = new Dictionary<long, TaxCodeRate>();
        foreach (var code in taxCodes.Where(c => c.UseYn))
        {
            rates[code.Id] = await taxRates.GetRateAsync(code, request.PoDt, transaction, cancellationToken);
        }

        var rounder = new AmountRounder(currencies.Select(c => new Currency(c.CurrencyCode, c.Decimals)));
        var (computed, totals) = PoEntryRules.Calculate(
            header, lines, previousCurrencyCode, new PurchaseOrderMasters(itemTypes, warehouses, rates), settings, rounder);
        return new PurchaseOrderWrite(header, totals, computed, status, revisionNo);
    }

    private async Task<PurchaseOrderDto> ReadAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var r = await orders.FindAsync(id, forUpdate: false, transaction, cancellationToken) ?? throw new NotFoundException(NotFoundCode, id);
        var lines = await orders.FindLinesAsync(id, transaction, cancellationToken);
        return new PurchaseOrderDto(
            r.Id, r.PoNo, r.Status, r.VendorId, r.VendorName, r.PoDt, r.CurrencyCode, r.ExchangeRate, r.Incoterms, r.DueDt, r.WarehouseId,
            r.PaymentTerms, r.Buyer, r.Remarks, lines.Sum(l => l.GrossAmt), lines.Sum(l => l.DcAmt), r.SupplyAmt, r.TaxAmt, r.TotPoAmt,
            r.TotBaseAmt, r.RevisionNo, r.ApprovedAt, r.ApprovedBy, r.ClosedAt, r.ClosedBy, r.CancelledAt, r.CancelledBy, r.ReceivedQty,
            lines.Select(l => new PurchaseOrderLineDto(
                l.Id, l.Seq, l.ItemId, l.ItemCd, l.ItemName, l.PoQty, l.PoPrc, l.GrossAmt, l.DcRate, l.DcAmt, l.PoAmt, l.TaxCodeId,
                l.SupplyAmt, l.TaxAmt, l.DueDt, l.WarehouseId, l.ReceivedQty)).ToList(),
            r.UpdatedAt);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
