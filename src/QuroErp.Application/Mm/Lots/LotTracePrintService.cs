using System.Globalization;
using QuroErp.Application.Bc.Companies;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Common.Printing;
using QuroErp.Application.Mm.PurchaseOrders;
using QuroErp.Contracts.Mm.Lots;
using QuroErp.Domain.Bc.Localization;

namespace QuroErp.Application.Mm.Lots;

/// <summary>A search condition printed above the rows: a print resource key and its value.</summary>
public sealed record LotTraceCondition(string LabelKey, string? Value);

/// <summary>
/// What the Lot trace report prints (MM-BR-369): the company head, the direction (LotTraceRoutes.Forward or Backward), the
/// conditions, the rows exactly as the screen gets them and the MM qty decimals.
/// </summary>
public sealed record LotTracePrint(
    PrintCompany Company, string Direction, IReadOnlyList<LotTraceCondition> Conditions, IReadOnlyList<LotTraceRowDto> Rows,
    int QtyDecimals);

/// <summary>Renders the Lot trace report (Infrastructure: HTML template → PDF, one XLSX sheet).</summary>
public interface ILotTraceDocument
{
    Task<byte[]> PdfAsync(LotTracePrint print, PrintPaper paper, CultureInfo culture, CancellationToken cancellationToken);

    byte[] Excel(LotTracePrint print, CultureInfo culture);
}

/// <summary>Lot trace report as PDF or XLSX with the same rows as the screen (MM-BR-369).</summary>
public sealed class LotTracePrintService(
    LotTraceService trace,
    ICompanyRepository companies,
    ILocalizationRepository localization,
    ILotTraceDocument document,
    IUnitOfWorkFactory unitOfWork)
{
    private const string FileName = "lot-trace";

    /// <exception cref="QuroErp.Domain.Common.DomainException">The checks of <see cref="LotTraceService.ForwardAsync"/>.</exception>
    public async Task<PrintFile> ForwardAsync(LotTraceForwardQuery query, bool pdf, CultureInfo culture, CancellationToken cancellationToken)
    {
        var rows = await trace.ForwardAsync(query, cancellationToken);
        LotTraceCondition[] conditions =
        [
            new("Print.LotTrace.Item", (rows.Count > 0 ? rows[0].ItemCd : null) ?? query.ItemId?.ToString(CultureInfo.InvariantCulture)),
            new("Print.LotTrace.LotNo", query.LotNo?.Trim()),
        ];
        return await RenderAsync(LotTraceRoutes.Forward, conditions, rows, pdf, culture, cancellationToken);
    }

    /// <exception cref="QuroErp.Domain.Common.DomainException">The checks of <see cref="LotTraceService.BackwardAsync"/>.</exception>
    public async Task<PrintFile> BackwardAsync(LotTraceBackwardQuery query, bool pdf, CultureInfo culture, CancellationToken cancellationToken)
    {
        var rows = await trace.BackwardAsync(query, cancellationToken);
        var conditions = new List<LotTraceCondition>();
        if (!string.IsNullOrWhiteSpace(query.DocNo))
        {
            conditions.Add(new("Print.LotTrace.DocNo", query.DocNo.Trim().ToUpperInvariant()));
        }
        else if (query.ShipmentLineId is null && query.InvoiceLineId is null && query.CustomerId is not null)
        {
            conditions.Add(new("Print.LotTrace.Customer", (rows.Count > 0 ? rows[0].PartyName : null) ?? query.CustomerId.Value.ToString(CultureInfo.InvariantCulture)));
            conditions.Add(new("Print.LotTrace.Period", $"{query.From?.ToString("d", culture)} ~ {query.To?.ToString("d", culture)}"));
        }
        return await RenderAsync(LotTraceRoutes.Backward, conditions, rows, pdf, culture, cancellationToken);
    }

    private async Task<PrintFile> RenderAsync(
        string direction, IReadOnlyList<LotTraceCondition> conditions, IReadOnlyList<LotTraceRowDto> rows, bool pdf, CultureInfo culture,
        CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var tx = uow.Transaction;
        var company = await companies.FindAsync(forUpdate: false, tx, cancellationToken);
        var logo = await companies.FindLogoAsync(tx, cancellationToken);
        var place = await localization.FindCompanyAsync(tx, cancellationToken);
        var settings = await localization.FindDecimalSettingsAsync(tx, cancellationToken);
        await uow.CommitAsync(cancellationToken);

        var country = place?.CountryCode ?? CountryProfiles.Us;
        var policy = new DecimalPolicy(
            country, settings.Select(s => new DecimalSetting(s.Module, s.QtyDecimals, Rounding.ParseMode(s.RoundingMode))), [], new Dictionary<string, string?>());
        var print = new LotTracePrint(
            new PrintCompany(company?.Name ?? string.Empty, [], company?.TelNo, company?.Email, logo?.LogoImage, logo?.LogoContentType),
            direction, conditions, rows, policy.SettingOf(DecimalModule.Mm).QtyDecimals);
        var name = $"{FileName}-{direction}";
        return pdf
            ? new PrintFile(await document.PdfAsync(print, PrintPaper.ForCountry(place?.CountryCode), culture, cancellationToken), name + ".pdf",
                PurchaseOrderPrintService.PdfType)
            : new PrintFile(document.Excel(print, culture), name + ".xlsx", PurchaseOrderPrintService.XlsxType);
    }
}
