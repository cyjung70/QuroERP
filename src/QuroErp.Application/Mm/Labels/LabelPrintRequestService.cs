using System.Data;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Mm.Labels;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Labels;

namespace QuroErp.Application.Mm.Labels;

/// <summary>Conditions of the print targets of one label type. Ids keeps only those targets, for a print.</summary>
public sealed record LabelTargetFilter(long? GoodsReceiptId, long? ItemId, long? WarehouseId, IReadOnlyCollection<long>? Ids, int Limit);

/// <summary>What can be printed (mm_pallet, bc_item, mm_lot, bc_location).</summary>
public interface ILabelTargetRepository
{
    /// <summary>
    /// The targets of the label type in code order: ACTIVE Pallets, items in use, Lots, Locations in use other than the system
    /// Location '*'.
    /// </summary>
    Task<IReadOnlyList<LabelTargetDto>> FindAsync(
        string labelType, LabelTargetFilter filter, IDbTransaction transaction, CancellationToken cancellationToken);
}

/// <summary>The print history read back (mm_label_print, MM-BR-919).</summary>
public interface ILabelPrintHistoryRepository
{
    /// <summary>Prints at or after printedFrom and before printedBefore, of the label type when given, newest first.</summary>
    Task<IReadOnlyList<LabelPrintHistoryDto>> FindAsync(
        DateTimeOffset? printedFrom, DateTimeOffset? printedBefore, string? labelType, int limit, IDbTransaction transaction, CancellationToken cancellationToken);
}

/// <summary>
/// Barcode label printing (MM-UC-037): the targets of a label type, a print of chosen targets with their own copies in the label
/// type's format (MM-BR-918, MM-BR-919) and the print history.
/// </summary>
public sealed class LabelPrintRequestService(
    ILabelTargetRepository targets,
    ILabelFormatRepository formats,
    ILabelPrintHistoryRepository history,
    LabelPrintService printer,
    IBusinessClock clock,
    IUnitOfWorkFactory unitOfWork)
{
    /// <summary>A value is missing or out of range. Arg: the field.</summary>
    public const string RequiredCode = "Common-E-005";

    /// <summary>An unknown label type or output kind. Args: the field, the value.</summary>
    public const string InvalidCode = "Common-E-002";

    /// <summary>The label format is not in use, or a target does not exist. Arg: the label type or the target id.</summary>
    public const string NotFoundCode = "Common-E-006";

    /// <summary>The most copies of one target in a print, so a typing slip cannot make thousands of labels.</summary>
    public const int MaxCopies = 999;

    /// <summary>The targets of the label type, at most <see cref="LabelPrintRoutes.MaxListSize"/>.</summary>
    /// <exception cref="DomainException">422 Common-E-005 without a label type, or a LOCATION list without a warehouse; Common-E-002 for an unknown type.</exception>
    public async Task<IReadOnlyList<LabelTargetDto>> TargetsAsync(LabelTargetQuery query, CancellationToken cancellationToken)
    {
        var labelType = LabelTypeOf(query.LabelType);
        if (labelType == LabelType.Location && query.WarehouseId is null)
        {
            throw new DomainException(RequiredCode, "warehouseId");
        }
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var rows = await targets.FindAsync(
            labelType, new LabelTargetFilter(query.GoodsReceiptId, query.ItemId, query.WarehouseId, null, LabelPrintRoutes.MaxListSize),
            uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return rows;
    }

    /// <summary>
    /// Prints the targets in the order given, each its own copies, in the format of the label type (the request's output kind
    /// replaces the format's). One history row per target.
    /// </summary>
    /// <exception cref="DomainException">
    /// 422 Common-E-005 (labelType, targets, copies above 0 and at most <see cref="MaxCopies"/>); Common-E-002 (labelType,
    /// outputKind) for an unknown value.
    /// </exception>
    /// <exception cref="NotFoundException">404 Common-E-006 when the format is not in use or a target does not exist.</exception>
    public async Task<LabelOutput> PrintAsync(LabelPrintRequest request, string user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var labelType = LabelTypeOf(request.LabelType);
        if (request.Targets is not { Count: > 0 } chosen)
        {
            throw new DomainException(RequiredCode, "targets");
        }
        if (chosen.Count > LabelPrintRoutes.MaxListSize || chosen.Any(t => t.Copies <= 0 || t.Copies > MaxCopies))
        {
            throw new DomainException(RequiredCode, "copies");
        }
        var outputKind = request.OutputKind?.Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(outputKind) && !LabelFormatRules.OutputKinds.Contains(outputKind))
        {
            throw new DomainException(InvalidCode, "outputKind", request.OutputKind!);
        }

        LabelFormat format;
        Dictionary<long, LabelTargetDto> found;
        await using (var uow = await unitOfWork.BeginAsync(cancellationToken))
        {
            format = await formats.FindAsync(labelType, uow.Transaction, cancellationToken) ?? throw new NotFoundException(NotFoundCode, labelType);
            var ids = chosen.Select(t => t.Id).Distinct().ToList();
            found = (await targets.FindAsync(labelType, new LabelTargetFilter(null, null, null, ids, ids.Count), uow.Transaction, cancellationToken))
                .ToDictionary(t => t.Id);
            await uow.CommitAsync(cancellationToken);
        }
        if (chosen.FirstOrDefault(t => !found.ContainsKey(t.Id)) is { } missing)
        {
            throw new NotFoundException(NotFoundCode, missing.Id);
        }
        if (!string.IsNullOrEmpty(outputKind))
        {
            format = format with { OutputKind = outputKind };
        }
        var labels = chosen.Select(t => new LabelCopies(Label(found[t.Id]), t.Copies)).ToList();
        var printerName = string.IsNullOrWhiteSpace(request.PrinterName) ? null : request.PrinterName.Trim();
        return await printer.PrintEachAsync(format, labels, printerName, user, cancellationToken);
    }

    /// <summary>Prints on the company dates from..to of the label type, newest first, at most <see cref="LabelPrintRoutes.MaxListSize"/>.</summary>
    public async Task<IReadOnlyList<LabelPrintHistoryDto>> HistoryAsync(LabelPrintHistoryQuery query, CancellationToken cancellationToken)
    {
        var labelType = string.IsNullOrWhiteSpace(query.LabelType) ? null : query.LabelType.Trim().ToUpperInvariant();
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var rows = await history.FindAsync(
            query.From is { } from ? StartOf(from) : null, query.To is { } to ? StartOf(to.AddDays(1)) : null, labelType,
            LabelPrintRoutes.MaxListSize, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return rows;
    }

    /// <summary>The label of a target with its field values and DataMatrix (MM-BR-918).</summary>
    public static Label Label(LabelTargetDto target) => target.LabelType switch
    {
        LabelType.Pallet => LabelValues.Pallet(
            target.Id, target.Code, target.ItemCd ?? "", target.Name, target.LotNo ?? "", target.Qty ?? 0, target.ExpiryDt),
        LabelType.Item => LabelValues.Item(target.Id, target.Code, target.Name),
        LabelType.Lot => LabelValues.Lot(target.Id, target.ItemCd ?? "", target.Name, target.Code, target.ExpiryDt),
        LabelType.Location => LabelValues.Location(target.Id, target.WarehouseCode ?? "", target.Code),
        _ => throw new ArgumentException($"Unknown label type {target.LabelType}.", nameof(target)),
    };

    private static string LabelTypeOf(string? value)
    {
        var labelType = value?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(labelType))
        {
            throw new DomainException(RequiredCode, "labelType");
        }
        return LabelFormatRules.IsType(labelType) ? labelType : throw new DomainException(InvalidCode, "labelType", value!);
    }

    // Midnight of the company date, in UTC for timestamptz
    private DateTimeOffset StartOf(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, clock.TimeZone.GetUtcOffset(local)).ToUniversalTime();
    }
}
