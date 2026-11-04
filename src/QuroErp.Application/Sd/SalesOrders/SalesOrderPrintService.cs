using System.Globalization;
using QuroErp.Application.Bc.Companies;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Common.Printing;
using QuroErp.Application.Mm.PurchaseOrders;
using QuroErp.Application.Sd.Customers;
using QuroErp.Contracts.Sd.SalesOrders;
using QuroErp.Domain.Bc.Localization;

namespace QuroErp.Application.Sd.SalesOrders;

/// <summary>A name and address block of a printed sales document.</summary>
public sealed record SalesOrderPrintParty(string Name, IReadOnlyList<string> AddressLines, string? Phone);

/// <summary>
/// What the sales order documents print (print-documents.md): the stored order as read by the API (amounts are not computed
/// again), the company head, bill-to and ship-to, and the decimals of amounts (document currency), unit prices (currency country
/// × SD) and quantities (SD).
/// </summary>
public sealed record SalesOrderPrint(
    PrintCompany Company, SalesOrderPrintParty BillTo, SalesOrderPrintParty ShipTo, SalesOrderDto Order, int AmountDecimals, int PriceDecimals,
    int QtyDecimals);

/// <summary>Picking list: lines with item location and quantity; no amounts.</summary>
public sealed record SalesOrderPickingPrint(SalesOrderPrint Order, IReadOnlyList<SalesOrderPickingRow> Lines);

/// <summary>Set component list: the components of each Set line, quantity = order quantity × component quantity; no amounts.</summary>
public sealed record SalesOrderSetComponentPrint(SalesOrderPrint Order, IReadOnlyList<SalesOrderSetComponentRow> Components);

/// <summary>Renders the sales order documents as PDF (Infrastructure: HTML templates).</summary>
public interface ISalesOrderDocument
{
    Task<byte[]> SalesOrderPdfAsync(SalesOrderPrint print, PrintPaper paper, CultureInfo culture, CancellationToken cancellationToken);

    Task<byte[]> PickingListPdfAsync(SalesOrderPickingPrint print, PrintPaper paper, CultureInfo culture, CancellationToken cancellationToken);

    Task<byte[]> SetComponentListPdfAsync(SalesOrderSetComponentPrint print, PrintPaper paper, CultureInfo culture, CancellationToken cancellationToken);
}

/// <summary>Sales order, picking list and set component list PDFs (print-documents.md).</summary>
public sealed class SalesOrderPrintService(
    SalesOrderService orders,
    ISalesOrderRepository repository,
    ICustomerRepository customers,
    ICompanyRepository companies,
    ILocalizationRepository localization,
    ISalesOrderDocument document,
    IUnitOfWorkFactory unitOfWork)
{
    public const string PdfType = "application/pdf";

    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task<PrintFile> SalesOrderPdfAsync(long id, CultureInfo culture, CancellationToken cancellationToken)
    {
        var (print, country) = await LoadAsync(id, cancellationToken);
        var pdf = await document.SalesOrderPdfAsync(print, PrintPaper.ForCountry(country), culture, cancellationToken);
        return new PrintFile(pdf, print.Order.SoNo + ".pdf", PdfType);
    }

    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task<PrintFile> PickingListPdfAsync(long id, CultureInfo culture, CancellationToken cancellationToken)
    {
        var (print, country) = await LoadAsync(id, cancellationToken);
        var lines = await ReadAsync((tx, ct) => repository.FindPickingAsync(id, tx, ct), cancellationToken);
        var pdf = await document.PickingListPdfAsync(new SalesOrderPickingPrint(print, lines), PrintPaper.ForCountry(country), culture, cancellationToken);
        return new PrintFile(pdf, print.Order.SoNo + "-picking.pdf", PdfType);
    }

    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task<PrintFile> SetComponentListPdfAsync(long id, CultureInfo culture, CancellationToken cancellationToken)
    {
        var (print, country) = await LoadAsync(id, cancellationToken);
        var components = await ReadAsync((tx, ct) => repository.FindSetComponentsAsync(id, tx, ct), cancellationToken);
        var pdf = await document.SetComponentListPdfAsync(
            new SalesOrderSetComponentPrint(print, components), PrintPaper.ForCountry(country), culture, cancellationToken);
        return new PrintFile(pdf, print.Order.SoNo + "-set-components.pdf", PdfType);
    }

    private async Task<T> ReadAsync<T>(Func<System.Data.IDbTransaction, CancellationToken, Task<T>> read, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var result = await read(uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return result;
    }

    private async Task<(SalesOrderPrint Print, string? Country)> LoadAsync(long id, CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(id, cancellationToken);
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var tx = uow.Transaction;
        var company = await companies.FindAsync(forUpdate: false, tx, cancellationToken);
        var logo = await companies.FindLogoAsync(tx, cancellationToken);
        var customer = await customers.FindAsync(order.CustomerId, forUpdate: false, tx, cancellationToken);
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
        var c = customer?.Fields;
        var print = new SalesOrderPrint(
            new PrintCompany(
                company?.Name ?? string.Empty, Lines(company?.Billto), company?.TelNo, company?.Email, logo?.LogoImage, logo?.LogoContentType),
            new SalesOrderPrintParty(
                order.CustomerName ?? string.Empty, c is null ? [] : Address(c.Addr1, c.Addr2, Join(c.City, c.State, c.ZipCode), c.Country), c?.Phone1),
            new SalesOrderPrintParty(
                order.ShipName ?? order.CustomerName ?? string.Empty,
                Address(order.ShipAddr1, order.ShipAddr2, Join(order.ShipCity, order.ShipState, order.ShipZipCode), order.ShipCountry), order.ShipPhone),
            order,
            currencies.FirstOrDefault(x => x.CurrencyCode == order.CurrencyCode)?.Decimals ?? 2,
            policy.UnitPriceDecimalsOf(DecimalModule.Sd, order.CurrencyCode),
            policy.SettingOf(DecimalModule.Sd).QtyDecimals);
        return (print, place?.CountryCode);
    }

    private static IReadOnlyList<string> Address(params string?[] lines) =>
        [.. lines.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l!.Trim())];

    private static IReadOnlyList<string> Lines(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : [.. text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0)];

    private static string? Join(params string?[] parts) =>
        string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim())) is { Length: > 0 } s ? s : null;
}
