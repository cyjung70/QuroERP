using System.Globalization;
using QuroErp.Application.Bc.BusinessPlaces;
using QuroErp.Application.Bc.Companies;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Common.Printing;
using QuroErp.Application.Mm.PurchaseOrders;
using QuroErp.Application.Sd.Customers;
using QuroErp.Application.Sd.SalesOrders;
using QuroErp.Contracts.Sd.SalesInvoices;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Sd.Customers;
using QuroErp.Domain.Sd.SalesInvoices;

namespace QuroErp.Application.Sd.SalesInvoices;

/// <summary>
/// What the sales invoice prints: the stored invoice as read by the API (amounts are not computed again), the company head, bill-to
/// and ship-to, and the decimals of amounts (document currency), unit prices (currency country × SD) and quantities (SD).
/// ShowDiscount is true when a line has a discount (sales-invoice.md "기본 흐름" 7).
/// </summary>
public sealed record SalesInvoicePrint(
    PrintCompany Company, SalesOrderPrintParty BillTo, SalesOrderPrintParty ShipTo, SalesInvoiceDto Invoice, int AmountDecimals, int PriceDecimals,
    int QtyDecimals, bool ShowDiscount);

/// <summary>A party of the KR tax invoice: business number, name, CEO, address, business type and item (SD-BR-134).</summary>
public sealed record TaxInvoiceParty(
    string? BusinessRegNo, string Name, string? CeoName, string? Address, string? BusinessType, string? BusinessItem);

/// <summary>
/// The KR tax invoice (SD-BR-134): written on the invoice date, the supplier is the invoice's business place (BC-BR-320) and the
/// buyer the customer; each line's supply and tax and the totals. The printed number is the invoice number.
/// </summary>
public sealed record TaxInvoicePrint(PrintCompany Company, TaxInvoiceParty Supplier, TaxInvoiceParty Buyer, SalesInvoiceDto Invoice, int AmountDecimals, int QtyDecimals);

/// <summary>Renders the sales invoice and the KR tax invoice as PDF (Infrastructure: HTML templates).</summary>
public interface ISalesInvoiceDocument
{
    Task<byte[]> SalesInvoicePdfAsync(SalesInvoicePrint print, PrintPaper paper, CultureInfo culture, CancellationToken cancellationToken);

    Task<byte[]> TaxInvoicePdfAsync(TaxInvoicePrint print, PrintPaper paper, CultureInfo culture, CancellationToken cancellationToken);
}

/// <summary>Sales invoice and KR tax invoice PDFs (print-documents.md "sales-invoice", SD-BR-133, SD-BR-134).</summary>
public sealed class SalesInvoicePrintService(
    SalesInvoiceService invoices,
    ISalesInvoiceRepository repository,
    ICustomerRepository customers,
    ICompanyRepository companies,
    IBusinessPlaceRepository businessPlaces,
    ILocalizationRepository localization,
    ISalesInvoiceDocument document,
    IUnitOfWorkFactory unitOfWork)
{
    public const string PdfType = "application/pdf";

    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task<PrintFile> SalesInvoicePdfAsync(long id, CultureInfo culture, CancellationToken cancellationToken)
    {
        var invoice = await invoices.GetAsync(id, cancellationToken);
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var tx = uow.Transaction;
        var company = await companies.FindAsync(forUpdate: false, tx, cancellationToken);
        var logo = await companies.FindLogoAsync(tx, cancellationToken);
        var customer = await customers.FindAsync(invoice.CustomerId, forUpdate: false, tx, cancellationToken);
        var place = await localization.FindCompanyAsync(tx, cancellationToken);
        var policy = await PolicyAsync(place?.CountryCode, tx, cancellationToken);
        var currencies = await localization.FindCurrenciesAsync(tx, cancellationToken);
        await uow.CommitAsync(cancellationToken);

        var c = customer?.Fields;
        var print = new SalesInvoicePrint(
            new PrintCompany(
                company?.Name ?? string.Empty, Lines(company?.Billto), company?.TelNo, company?.Email, logo?.LogoImage, logo?.LogoContentType),
            new SalesOrderPrintParty(
                invoice.CustomerName ?? string.Empty, c is null ? [] : Address(c.Addr1, c.Addr2, Join(c.City, c.State, c.ZipCode), c.Country), c?.Phone1),
            new SalesOrderPrintParty(
                invoice.ShipName ?? invoice.CustomerName ?? string.Empty,
                Address(invoice.ShipAddr1, invoice.ShipAddr2, Join(invoice.ShipCity, invoice.ShipState, invoice.ShipZipCode), invoice.ShipCountry),
                invoice.ShipPhone),
            invoice,
            currencies.FirstOrDefault(x => x.CurrencyCode == invoice.CurrencyCode)?.Decimals ?? 2,
            policy.UnitPriceDecimalsOf(DecimalModule.Sd, invoice.CurrencyCode),
            policy.SettingOf(DecimalModule.Sd).QtyDecimals,
            invoice.Lines.Any(l => l.DcRate != 0));
        var pdf = await document.SalesInvoicePdfAsync(print, PrintPaper.ForCountry(place?.CountryCode), culture, cancellationToken);
        return new PrintFile(pdf, invoice.DnNo + ".pdf", PdfType);
    }

    /// <summary>SD-BR-134: the KR tax invoice of a taxable invoice to a business customer.</summary>
    /// <exception cref="NotFoundException">Common-E-006, also for a company outside KR.</exception>
    /// <exception cref="DomainException">SD-E-134 when the invoice has no taxable line or the customer or business place lacks its business details.</exception>
    public async Task<PrintFile> TaxInvoicePdfAsync(long id, CultureInfo culture, CancellationToken cancellationToken)
    {
        var invoice = await invoices.GetAsync(id, cancellationToken);
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var tx = uow.Transaction;
        var place = await localization.FindCompanyAsync(tx, cancellationToken);
        if (place?.CountryCode != CountryProfiles.Kr)
        {
            throw new NotFoundException(SalesInvoiceService.NotFoundCode, id);
        }
        var row = await repository.FindAsync(id, forUpdate: false, tx, cancellationToken) ?? throw new NotFoundException(SalesInvoiceService.NotFoundCode, id);
        var taxLines = await repository.FindTaxLinesAsync(id, tx, cancellationToken);
        var company = await companies.FindAsync(forUpdate: false, tx, cancellationToken);
        var logo = await companies.FindLogoAsync(tx, cancellationToken);
        var customer = await customers.FindAsync(invoice.CustomerId, forUpdate: false, tx, cancellationToken);
        var supplier = row.BusinessPlaceId is { } placeId ? await businessPlaces.FindAsync(placeId, forUpdate: false, tx, cancellationToken) : null;
        var currencies = await localization.FindCurrenciesAsync(tx, cancellationToken);
        var settings = await localization.FindDecimalSettingsAsync(tx, cancellationToken);
        await uow.CommitAsync(cancellationToken);

        var c = customer?.Fields;
        SalesInvoiceRules.EnsureTaxInvoice(
            invoice.DnNo, taxLines.Select(t => t.TaxKind), c?.TaxPartyType, c?.BusinessRegNo, customer?.CName, c?.CeoName);
        SalesInvoiceRules.EnsureTaxInvoice(
            invoice.DnNo, taxLines.Select(t => t.TaxKind), Customer.PartyBusiness, supplier?.BusinessRegNo,
            supplier?.BusinessPlaceName, supplier?.CeoName);
        var print = new TaxInvoicePrint(
            new PrintCompany(
                company?.Name ?? string.Empty, Lines(company?.Billto), company?.TelNo, company?.Email, logo?.LogoImage, logo?.LogoContentType),
            new TaxInvoiceParty(
                supplier!.BusinessRegNo, supplier.BusinessPlaceName, supplier.CeoName, Join(supplier.Addr1, supplier.Addr2, supplier.City),
                supplier.BusinessType, supplier.BusinessItem),
            new TaxInvoiceParty(
                c!.BusinessRegNo, customer!.CName, c.CeoName, Join(c.Addr1, c.Addr2, c.City), c.BusinessType, c.BusinessItem),
            invoice,
            currencies.FirstOrDefault(x => x.CurrencyCode == invoice.CurrencyCode)?.Decimals ?? 0,
            settings.FirstOrDefault(s => s.Module == DecimalModule.Sd)?.QtyDecimals ?? 0);
        var pdf = await document.TaxInvoicePdfAsync(print, PrintPaper.ForCountry(place.CountryCode), culture, cancellationToken);
        return new PrintFile(pdf, invoice.DnNo + "-tax-invoice.pdf", PdfType);
    }

    private async Task<DecimalPolicy> PolicyAsync(string? country, System.Data.IDbTransaction tx, CancellationToken cancellationToken)
    {
        var currencies = await localization.FindCurrenciesAsync(tx, cancellationToken);
        var settings = await localization.FindDecimalSettingsAsync(tx, cancellationToken);
        var countryDecimals = await localization.FindCountryDecimalsAsync(tx, cancellationToken);
        return new DecimalPolicy(
            country ?? CountryProfiles.Us,
            settings.Select(s => new DecimalSetting(s.Module, s.QtyDecimals, Rounding.ParseMode(s.RoundingMode))),
            countryDecimals.Select(d => new CountryDecimal(d.CountryCode, d.Module, d.UnitPriceDecimals)),
            currencies.ToDictionary(x => x.CurrencyCode, x => x.CountryCode));
    }

    private static IReadOnlyList<string> Address(params string?[] lines) =>
        [.. lines.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l!.Trim())];

    private static IReadOnlyList<string> Lines(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : [.. text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0)];

    private static string? Join(params string?[] parts) =>
        string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim())) is { Length: > 0 } s ? s : null;
}
