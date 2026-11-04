using FluentAssertions;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Bc.Tax;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.GoodsReceipts;
using QuroErp.Domain.Mm.SupplierInvoices;

namespace QuroErp.Domain.Tests.Mm.SupplierInvoices;

/// <summary>Supplier invoice rules (MM-UC-032-T07, docs/business-rules/MM/supplier-invoice.md).</summary>
public class SupplierInvoiceRulesTests
{
    private const long Vendor = 9;
    private const long Vat10 = 1;
    private const long NoTax = 2;
    private const long SalesTax8 = 3;
    private const long GrLine = 100;
    private const long ServiceLine = 101;
    private const long EurLine = 102;

    private static readonly DateOnly InvoiceDt = new(2026, 11, 20);
    private static readonly AmountRounder Rounder = new([new Currency("USD", 2), new Currency("EUR", 2), new Currency("KRW", 0)]);
    private static readonly SupplierInvoiceSettings UsdBase = new("USD", TaxCalcMode.Document);

    private static readonly DecimalPolicy Decimals = new(
        "US", [new DecimalSetting(DecimalModule.Mm, 2, RoundingMode.HalfUp)], [new CountryDecimal("US", DecimalModule.Mm, 2)],
        new Dictionary<string, string?> { ["USD"] = "US", ["EUR"] = null });

    // A receipt of 10 at 100 with 10% VAT; a service line; a EUR receipt line
    private static SupplierInvoiceMasters Masters(decimal invoiced = 0m, decimal returned = 0m, decimal? poPrc = null, decimal? dcRate = null) => new(
        new Dictionary<long, InvoiceReceiptLine>
        {
            [GrLine] = Receipt(GrLine, "INVENTORY", invoiced: invoiced, returned: returned, poPrc: poPrc, dcRate: dcRate),
            [ServiceLine] = Receipt(ServiceLine, "SERVICE"),
            [EurLine] = Receipt(EurLine, "INVENTORY", currency: "EUR", rate: 1.1m),
        },
        new Dictionary<long, ReceiptTaxCode>
        {
            [Vat10] = new(new(Vat10, 10m, false), TaxKind.VatStandard),
            [NoTax] = new(new(NoTax, 0m, false), TaxKind.None),
            [SalesTax8] = new(new(SalesTax8, 8m, false), TaxKind.SalesTax),
        });

    private static InvoiceReceiptLine Receipt(
        long id, string itemType, string currency = "USD", decimal rate = 1m, decimal invoiced = 0m, decimal returned = 0m,
        decimal? poPrc = null, decimal? dcRate = null, long vendor = Vendor) =>
        new(id, "GR2611000001", (int)(id - 99), vendor, currency, rate, 1000 + id, itemType, 7, null, 10m, 100m, Vat10, TaxKind.VatStandard,
            1000m, 100m, true, invoiced, returned, poPrc, dcRate);

    private static SupplierInvoiceHeader Header(string currency = "USD", decimal rate = 1m, string? refNo = "INV-1") =>
        new(Vendor, refNo, InvoiceDt, InvoiceDt, null, currency, rate, false, null);

    private static SupplierInvoiceLineInput Line(decimal qty = 10m, decimal price = 100m, long grLine = GrLine, long taxCode = Vat10, bool deductible = true) =>
        new(grLine, qty, price, taxCode, deductible);

    private static StoredSupplierInvoiceLine Stored(int seq = 1, decimal qty = 4m, decimal price = 100m, decimal pv = 0m) =>
        new(500 + seq, seq, GrLine, 1100, qty, price, Vat10, true, pv);

    private static (IReadOnlyList<SupplierInvoiceLine> Lines, SupplierInvoiceTotals Totals) Calc(
        SupplierInvoiceHeader header, SupplierInvoiceMasters? masters = null, IReadOnlyList<StoredSupplierInvoiceLine>? kept = null,
        IReadOnlyDictionary<long, decimal>? released = null, params SupplierInvoiceLineInput[] lines) =>
        SupplierInvoiceRules.Calculate(
            header, kept ?? [], lines, (kept?.Count ?? 0) + 1, released ?? new Dictionary<long, decimal>(), masters ?? Masters(), UsdBase,
            Rounder, Decimals);

    private static string CodeOf(Action act) => act.Should().Throw<DomainException>().Which.Code;

    [Fact, Trait("Id", "MM-UT-751"), Trait("Rule", "MM-BR-751")]
    public void Lines_MustExist_AndComeFromTheVendorsReceiptsInTheInvoiceCurrency()
    {
        var other = Masters();
        var otherVendor = new SupplierInvoiceMasters(
            new Dictionary<long, InvoiceReceiptLine> { [GrLine] = Receipt(GrLine, "INVENTORY", vendor: 77) }, other.TaxCodes);

        CodeOf(() => Calc(Header())).Should().Be(SupplierInvoiceRules.ReceiptMismatchCode);
        CodeOf(() => Calc(Header(), otherVendor, lines: Line())).Should().Be(SupplierInvoiceRules.ReceiptMismatchCode);
        CodeOf(() => Calc(Header(), lines: Line(grLine: EurLine))).Should().Be(SupplierInvoiceRules.ReceiptMismatchCode);
        CodeOf(() => Calc(Header(), lines: Line(grLine: 999))).Should().Be(SupplierInvoiceRules.NotFoundCode);
        CodeOf(() => Calc(Header(refNo: " "), lines: Line())).Should().Be(SupplierInvoiceRules.RequiredCode);
    }

    [Fact, Trait("Id", "MM-UT-751"), Trait("Id", "MM-UT-756"), Trait("Rule", "MM-BR-756")]
    public void DocumentCurrency_IsTheReceiptsCurrency_AndCannotChangeWithSavedLines()
    {
        var m = Masters().ReceiptLines;

        SupplierInvoiceRules.DocumentCurrency(null, [m[GrLine], m[ServiceLine]]).Should().Be("USD");
        SupplierInvoiceRules.DocumentCurrency("USD", []).Should().Be("USD");
        CodeOf(() => SupplierInvoiceRules.DocumentCurrency(null, [m[GrLine], m[EurLine]])).Should().Be(SupplierInvoiceRules.ReceiptMismatchCode);
        CodeOf(() => SupplierInvoiceRules.DocumentCurrency("USD", [m[EurLine]])).Should().Be(SupplierInvoiceRules.ReceiptMismatchCode);
        CodeOf(() => SupplierInvoiceRules.DocumentCurrency(null, [])).Should().Be(SupplierInvoiceRules.ReceiptMismatchCode);
        CodeOf(() => Calc(Header(rate: 1.2m), lines: Line())).Should().Be(ExchangeRateRules.InvalidCode, "the base currency rate is 1");
    }

    [Fact, Trait("Id", "MM-UT-752"), Trait("Rule", "MM-BR-752")]
    public void Number_IsViYymmSixDigits() =>
        DocumentNumberFormat.SupplierInvoice.Next(InvoiceDt, "VI2611000041").Should().Be("VI2611000042");

    [Theory, Trait("Id", "MM-UT-753"), Trait("Rule", "MM-BR-753")]
    [InlineData(0, 0, "0", false)]
    [InlineData(0, 0, "10", true)]
    [InlineData(0, 0, "10.01", false)]
    [InlineData(4, 0, "6", true)]
    [InlineData(4, 1, "6", false)]
    [InlineData(4, 1, "5", true)]
    public void Qty_IsAboveZero_AndAtMostTheOpenQty(int invoiced, int returned, string qty, bool ok)
    {
        var act = () => Calc(Header(), Masters(invoiced, returned), lines: Line(decimal.Parse(qty, System.Globalization.CultureInfo.InvariantCulture)));

        if (ok)
        {
            act.Should().NotThrow();
        }
        else
        {
            act.Should().Throw<DomainException>().Which.Code.Should().BeOneOf(SupplierInvoiceRules.QtyCode, DecimalPolicy.TooManyDecimalsCode);
        }
    }

    [Fact, Trait("Id", "MM-UT-753"), Trait("Rule", "MM-BR-753")]
    public void Qty_CountsEveryNewLineOfAReceiptLine_AndWhatDeletedLinesGiveBack()
    {
        var act = () => Calc(Header(), Masters(invoiced: 4m), lines: [Line(4m), Line(3m)]);
        var withReleased = () => Calc(Header(), Masters(invoiced: 4m), released: new Dictionary<long, decimal> { [GrLine] = 4m }, lines: [Line(4m), Line(3m)]);

        act.Should().Throw<DomainException>().Which.Args.Should().Equal(1, 6m);
        withReleased.Should().NotThrow();
    }

    [Fact, Trait("Id", "MM-UT-754"), Trait("Rule", "MM-BR-754")]
    public void Price_CannotBeZero_AndADifferenceFromThePoNetPriceIsReported()
    {
        CodeOf(() => Calc(Header(), lines: Line(price: 0m))).Should().Be(SupplierInvoiceRules.PriceCode);

        // PO 120 less 12.5% = 105; without a PO the receipt price 100
        var masters = Masters(poPrc: 120m, dcRate: 12.5m);
        var (lines, _) = Calc(Header(), masters, lines: [Line(5m, 105m), Line(5m, 110m)]);
        var (direct, _) = Calc(Header(), lines: Line(5m, 100m));

        SupplierInvoiceRules.PriceDifferences(lines, masters.ReceiptLines, "USD", Decimals).Should().Equal(new PriceDifference(2, 110m, 105m));
        SupplierInvoiceRules.PriceDifferences(direct, Masters().ReceiptLines, "USD", Decimals).Should().BeEmpty();
        SupplierInvoiceRules.ReferencePrice(Receipt(GrLine, "INVENTORY", poPrc: 10m, dcRate: 33m), "USD", Decimals).Should().Be(6.70m);
    }

    [Fact, Trait("Id", "MM-UT-755"), Trait("Rule", "MM-BR-755")]
    public void Amounts_UseTheInvoiceTax_AndBalanceIsTotalLessPaid()
    {
        var (lines, totals) = Calc(Header(), lines: [Line(4m, 100m), Line(1m, 50m, ServiceLine, NoTax)]);

        lines.Select(l => (l.Seq, l.SupplyAmt, l.TaxAmt)).Should().Equal((1, 400m, 40m), (2, 50m, 0m));
        totals.Should().Be(new SupplierInvoiceTotals(450m, 40m, 490m, 490m));
        SupplierInvoiceRules.BalanceOf(490m, 90m).Should().Be(400m);
    }

    [Fact, Trait("Id", "MM-UT-758"), Trait("Rule", "MM-BR-758"), Trait("Id", "MM-UT-164"), Trait("Rule", "MM-BR-164")]
    public void PriceVariance_IsTheStockAmountDifference_OnlyForInventoryLines()
    {
        // (110 − 100) × 4 with deductible VAT; a service line and an unchanged price have none
        var (lines, _) = Calc(Header(), lines: [Line(4m, 110m), Line(1m, 120m, ServiceLine), Line(2m, 100m)]);
        // Non-deductible VAT goes into cost: 4 × 110 × 1.1 = 484 − 1000 × 4/10 (receipt VAT deductible)
        var (nonDeductible, _) = Calc(Header(), lines: Line(4m, 110m, deductible: false));
        // US sales tax goes into cost: 4 × 110 × 1.08 = 475.20 − 400 (receipt VAT deductible)
        var (salesTax, _) = Calc(Header(), lines: Line(4m, 110m, taxCode: SalesTax8));

        lines.Select(l => l.PvAmt).Should().Equal(40m, 0m, 0m);
        nonDeductible.Single().PvAmt.Should().Be(84m);
        salesTax.Single().PvAmt.Should().Be(75.20m);
    }

    [Fact, Trait("Id", "MM-UT-758"), Trait("Rule", "MM-BR-758")]
    public void PriceVariance_ConvertsWithEachDocumentsRate()
    {
        // Receipt 10 × 100 EUR at 1.1 = 1100 USD; invoice 5 × 100 EUR at 1.2 = 600 USD − 550
        var (lines, totals) = Calc(Header("EUR", 1.2m), lines: Line(5m, 100m, EurLine));

        lines.Single().PvAmt.Should().Be(50m);
        totals.TotBaseAmt.Should().Be(660m);
    }

    [Fact, Trait("Id", "MM-UT-760"), Trait("Rule", "MM-BR-760")]
    public void PaidInvoices_AndChangedSavedLines_AreRefused_AndSavedLinesKeepTheirVariance()
    {
        CodeOf(() => SupplierInvoiceRules.EnsureEditable(SupplierInvoiceType.Invoice, "VI2611000001", 10m)).Should().Be(SupplierInvoiceRules.LockedCode);
        CodeOf(() => SupplierInvoiceRules.EnsureUnchanged("VI2611000001", Stored(), Line(4m, 101m))).Should().Be(SupplierInvoiceRules.LockedCode);
        SupplierInvoiceRules.EnsureEditable(SupplierInvoiceType.Invoice, "VI2611000001", 0m);
        SupplierInvoiceRules.EnsureUnchanged("VI2611000001", Stored(), Line(4m, 100m));

        var (lines, _) = Calc(Header(), kept: [Stored(pv: 12m)], lines: Line(2m, 110m));

        lines.Select(l => (l.Id, l.Seq, l.PvAmt)).Should().Equal(((long?)501, 1, 12m), (null, 2, 20m));
    }

    [Theory, Trait("Id", "MM-UT-761"), Trait("Rule", "MM-BR-761")]
    [InlineData(false, "100", "100", SupplierInvoiceStatus.Open)]
    [InlineData(false, "100", "40", SupplierInvoiceStatus.PartiallyPaid)]
    [InlineData(false, "100", "0", SupplierInvoiceStatus.Closed)]
    [InlineData(false, "0", "0", SupplierInvoiceStatus.Void)]
    [InlineData(true, "100", "100", SupplierInvoiceStatus.Void)]
    [InlineData(false, "-50", "-50", SupplierInvoiceStatus.Open)]
    [InlineData(false, "-50", "-20", SupplierInvoiceStatus.PartiallyPaid)]
    public void Status_ComesFromTheAmounts(bool isVoid, string tot, string bal, string expected) =>
        SupplierInvoiceRules.StatusOf(
                isVoid, decimal.Parse(tot, System.Globalization.CultureInfo.InvariantCulture),
                decimal.Parse(bal, System.Globalization.CultureInfo.InvariantCulture))
            .Should().Be(expected);

    [Fact, Trait("Id", "MM-UT-762"), Trait("Rule", "MM-BR-762")]
    public void DebitMemos_AreChangedOnlyByTheirVendorReturn() =>
        CodeOf(() => SupplierInvoiceRules.EnsureEditable(SupplierInvoiceType.DebitMemo, "VI2611000002", 0m)).Should().Be(SupplierInvoiceRules.DebitMemoCode);
}
