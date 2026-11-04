using System.Globalization;
using QuroErp.Application.Bc.Companies;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Common.Printing;
using QuroErp.Contracts.Mm.PurchaseOrders;
using QuroErp.Domain.Bc.Localization;

namespace QuroErp.Application.Mm.PurchaseOrders;

/// <summary>Vendor block of the purchase order form: name, address lines and contacts.</summary>
public sealed record PurchaseOrderPrintVendor(string Name, IReadOnlyList<string> AddressLines, string? Contact, string? Phone, string? Fax, string? Email);

/// <summary>
/// What the purchase order form prints (print-documents.md): the stored order as read by the API (amounts are not computed
/// again), the company head, the vendor and the decimals of amounts (document currency), unit prices (currency country × MM)
/// and quantities (MM).
/// </summary>
public sealed record PurchaseOrderPrint(
    PrintCompany Company, PurchaseOrderPrintVendor Vendor, PurchaseOrderDto Order, int AmountDecimals, int PriceDecimals, int QtyDecimals);

/// <summary>A file to download.</summary>
public sealed record PrintFile(byte[] Content, string FileName, string ContentType);

/// <summary>Renders the purchase order form (Infrastructure: HTML template → PDF, ClosedXML sheet).</summary>
public interface IPurchaseOrderDocument
{
    Task<byte[]> PdfAsync(PurchaseOrderPrint print, PrintPaper paper, CultureInfo culture, CancellationToken cancellationToken);

    byte[] Excel(PurchaseOrderPrint print, CultureInfo culture);
}

/// <summary>Purchase order form as PDF or XLSX with the same content (print-documents.md, MM-BR-021 Rev. n).</summary>
public sealed class PurchaseOrderPrintService(
    PurchaseOrderService orders,
    IPurchaseOrderRepository repository,
    ICompanyRepository companies,
    ILocalizationRepository localization,
    IPurchaseOrderDocument document,
    IUnitOfWorkFactory unitOfWork)
{
    public const string PdfType = "application/pdf";
    public const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task<PrintFile> PdfAsync(long id, CultureInfo culture, CancellationToken cancellationToken)
    {
        var (print, country) = await LoadAsync(id, cancellationToken);
        var pdf = await document.PdfAsync(print, PrintPaper.ForCountry(country), culture, cancellationToken);
        return new PrintFile(pdf, print.Order.PoNo + ".pdf", PdfType);
    }

    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task<PrintFile> ExcelAsync(long id, CultureInfo culture, CancellationToken cancellationToken)
    {
        var (print, _) = await LoadAsync(id, cancellationToken);
        return new PrintFile(document.Excel(print, culture), print.Order.PoNo + ".xlsx", XlsxType);
    }

    private async Task<(PurchaseOrderPrint Print, string? Country)> LoadAsync(long id, CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(id, cancellationToken);
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var tx = uow.Transaction;
        var company = await companies.FindAsync(forUpdate: false, tx, cancellationToken);
        var logo = await companies.FindLogoAsync(tx, cancellationToken);
        var vendor = await repository.FindPrintVendorAsync(order.VendorId, tx, cancellationToken);
        var place = await localization.FindCompanyAsync(tx, cancellationToken);
        var currencies = await localization.FindCurrenciesAsync(tx, cancellationToken);
        var settings = await localization.FindDecimalSettingsAsync(tx, cancellationToken);
        var countryDecimals = await localization.FindCountryDecimalsAsync(tx, cancellationToken);
        await uow.CommitAsync(cancellationToken);

        var country = place?.CountryCode ?? CountryProfiles.Us;
        var policy = new DecimalPolicy(
            country,
            settings.Select(s => new DecimalSetting(s.Module, s.QtyDecimals, Rounding.ParseMode(s.RoundingMode))),
            countryDecimals.Select(d => new CountryDecimal(d.CountryCode, d.Module, d.UnitPriceDecimals)),
            currencies.ToDictionary(c => c.CurrencyCode, c => c.CountryCode));
        var print = new PurchaseOrderPrint(
            new PrintCompany(
                company?.Name ?? string.Empty, Lines(company?.Billto), company?.TelNo, company?.Email, logo?.LogoImage, logo?.LogoContentType),
            vendor is null
                ? new PurchaseOrderPrintVendor(order.VendorName ?? string.Empty, [], null, null, null, null)
                : new PurchaseOrderPrintVendor(
                    vendor.VName,
                    [.. new[] { vendor.Addr1, vendor.Addr2, Join(vendor.City, vendor.State, vendor.ZipCode), vendor.Country }
                        .Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l!.Trim())],
                    vendor.Contact, vendor.Phone1, vendor.FaxNo, vendor.Email),
            order,
            currencies.FirstOrDefault(c => c.CurrencyCode == order.CurrencyCode)?.Decimals ?? 2,
            policy.UnitPriceDecimalsOf(DecimalModule.Mm, order.CurrencyCode),
            policy.SettingOf(DecimalModule.Mm).QtyDecimals);
        return (print, place?.CountryCode);
    }

    private static IReadOnlyList<string> Lines(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : [.. text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0)];

    private static string? Join(params string?[] parts) =>
        string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim())) is { Length: > 0 } s ? s : null;
}
