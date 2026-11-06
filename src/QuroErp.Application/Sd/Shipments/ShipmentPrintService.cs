using System.Globalization;
using QuroErp.Application.Bc.Companies;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Common.Printing;
using QuroErp.Application.Mm.PurchaseOrders;
using QuroErp.Application.Sd.Customers;
using QuroErp.Application.Sd.SalesOrders;
using QuroErp.Contracts.Sd.Shipments;
using QuroErp.Domain.Bc.Localization;

namespace QuroErp.Application.Sd.Shipments;

/// <summary>
/// What the delivery note prints: the stored shipment as read by the API, its lines with warehouse and lot, the company head,
/// bill-to and ship-to and the SD quantity decimals. It has no amounts, like the picking list.
/// </summary>
public sealed record ShipmentPrint(
    PrintCompany Company, SalesOrderPrintParty BillTo, SalesOrderPrintParty ShipTo, ShipmentDto Shipment, IReadOnlyList<ShipmentPrintLine> Lines,
    int QtyDecimals);

/// <summary>Renders the delivery note as PDF (Infrastructure: HTML templates).</summary>
public interface IShipmentDocument
{
    Task<byte[]> DeliveryNotePdfAsync(ShipmentPrint print, PrintPaper paper, CultureInfo culture, CancellationToken cancellationToken);
}

/// <summary>Delivery note PDF of a shipment (shipment.md "기본 흐름" 7).</summary>
public sealed class ShipmentPrintService(
    ShipmentService shipments,
    IShipmentRepository repository,
    ICustomerRepository customers,
    ICompanyRepository companies,
    ILocalizationRepository localization,
    IShipmentDocument document,
    IUnitOfWorkFactory unitOfWork)
{
    public const string PdfType = "application/pdf";

    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task<PrintFile> DeliveryNotePdfAsync(long id, CultureInfo culture, CancellationToken cancellationToken)
    {
        var shipment = await shipments.GetAsync(id, cancellationToken);
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var tx = uow.Transaction;
        var lines = await repository.FindPrintLinesAsync(id, tx, cancellationToken);
        var company = await companies.FindAsync(forUpdate: false, tx, cancellationToken);
        var logo = await companies.FindLogoAsync(tx, cancellationToken);
        var customer = await customers.FindAsync(shipment.CustomerId, forUpdate: false, tx, cancellationToken);
        var place = await localization.FindCompanyAsync(tx, cancellationToken);
        var settings = await localization.FindDecimalSettingsAsync(tx, cancellationToken);
        await uow.CommitAsync(cancellationToken);

        var c = customer?.Fields;
        var print = new ShipmentPrint(
            new PrintCompany(
                company?.Name ?? string.Empty, Lines(company?.Billto), company?.TelNo, company?.Email, logo?.LogoImage, logo?.LogoContentType),
            new SalesOrderPrintParty(
                shipment.CustomerName ?? string.Empty, c is null ? [] : Address(c.Addr1, c.Addr2, Join(c.City, c.State, c.ZipCode), c.Country), c?.Phone1),
            new SalesOrderPrintParty(
                shipment.ShipName ?? shipment.CustomerName ?? string.Empty,
                Address(shipment.ShipAddr1, shipment.ShipAddr2, Join(shipment.ShipCity, shipment.ShipState, shipment.ShipZipCode), shipment.ShipCountry),
                shipment.ShipPhone),
            shipment, lines,
            settings.FirstOrDefault(s => s.Module == DecimalModule.Sd)?.QtyDecimals ?? 0);
        var pdf = await document.DeliveryNotePdfAsync(print, PrintPaper.ForCountry(place?.CountryCode), culture, cancellationToken);
        return new PrintFile(pdf, shipment.ShipNo + ".pdf", PdfType);
    }

    private static IReadOnlyList<string> Address(params string?[] lines) =>
        [.. lines.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l!.Trim())];

    private static IReadOnlyList<string> Lines(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : [.. text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0)];

    private static string? Join(params string?[] parts) =>
        string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim())) is { Length: > 0 } s ? s : null;
}
