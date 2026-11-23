using FluentAssertions;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Bc.SetItems;
using QuroErp.Domain.Bc.Tax;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.GoodsReceipts;
using QuroErp.Domain.Mm.Inventory;
using QuroErp.Domain.Mm.Lots;
using QuroErp.Domain.Mm.PurchaseOrders;

namespace QuroErp.Domain.Tests.Mm.GoodsReceipts;

/// <summary>Goods receipt entry rules (MM-UC-004-T05, docs/business-rules/MM/goods-receipt.md).</summary>
public class GoodsReceiptRulesTests
{
    private const long Vendor = 9;
    private const long Inventory = 1;
    private const long Service = 2;
    private const long Asset = 3;
    private const long Kit = 4;
    private const long SetItem = 5;
    private const long LotItem = 6;
    private const long Warehouse = 7;
    private const long Vat10 = 1;
    private const long NoTax = 2;
    private const long SalesTax8 = 3;
    private const long PoLineId = 50;

    private static readonly DateOnly GrDt = new(2026, 11, 4);
    private static readonly AmountRounder Rounder = new([new Currency("USD", 2), new Currency("KRW", 0)]);
    private static readonly GoodsReceiptSettings UsdBase = new("USD", TaxCalcMode.Document);
    private static readonly GoodsReceiptSettings KrwBase = new("KRW", TaxCalcMode.Document);

    private static GoodsReceiptMasters Masters(string poStatus = PoStatusCode.Approved, long poVendor = Vendor, decimal received = 0m) => new(
        new Dictionary<long, string>
        {
            [Inventory] = "INVENTORY",
            [Service] = "SERVICE",
            [Asset] = "ASSET",
            [Kit] = "KIT",
            [SetItem] = "SET",
            [LotItem] = "INVENTORY",
        },
        new HashSet<long> { Warehouse },
        new Dictionary<long, ReceiptTaxCode>
        {
            [Vat10] = new(new(Vat10, 10m, false), TaxKind.VatStandard),
            [NoTax] = new(new(NoTax, 0m, false), TaxKind.None),
            [SalesTax8] = new(new(SalesTax8, 8m, false), TaxKind.SalesTax),
        },
        new Dictionary<long, ReceiptPoLine> { [PoLineId] = PoLine(poStatus, poVendor, received) },
        new HashSet<long> { LotItem });

    private static ReceiptPoLine PoLine(string status = PoStatusCode.Approved, long vendor = Vendor, decimal received = 0m, decimal qty = 10m) =>
        new(PoLineId, 500, "PO2611000001", 1, status, vendor, "USD", 1m, qty, received);

    private static GoodsReceiptHeader Header(string currency = "USD", decimal rate = 1m, DateOnly? grDt = null) =>
        new(Vendor, grDt ?? GrDt, Warehouse, "DN-1", null, currency, rate);

    private static GoodsReceiptLineInput Line(
        decimal qty = 10m, decimal price = 100m, long item = Inventory, long taxCode = Vat10, long? warehouse = Warehouse,
        bool deductible = true, long? poLine = null, long? location = null, decimal? palletQty = null, LotAttributes? lot = null) =>
        new(item, null, warehouse, qty, price, taxCode, deductible, poLine, location, palletQty, lot);

    private static StoredGoodsReceiptLine Stored(
        int seq = 1, decimal qty = 10m, decimal price = 100m, decimal invoiced = 0m, decimal returned = 0m, long? location = 70,
        decimal? palletQty = null, LotAttributes? lot = null) =>
        new(100 + seq, seq, Inventory, null, Warehouse, 80, qty, price, Vat10, true, null, invoiced, returned, location, palletQty, lot);

    private static (IReadOnlyList<GoodsReceiptLine> Lines, GoodsReceiptTotals Totals) Calc(
        GoodsReceiptHeader header, GoodsReceiptSettings? settings = null, GoodsReceiptMasters? masters = null,
        IReadOnlyList<StoredGoodsReceiptLine>? kept = null, params GoodsReceiptLineInput[] lines) =>
        GoodsReceiptRules.Calculate(header, kept ?? [], lines, (kept?.Count ?? 0) + 1, masters ?? Masters(), settings ?? UsdBase, Rounder);

    private static string CodeOf(Action act) => act.Should().Throw<DomainException>().Which.Code;

    [Theory, Trait("Id", "MM-UT-101"), Trait("Rule", "MM-BR-101")]
    [InlineData("0")]
    [InlineData("-1")]
    public void Qty_MustBeAboveZero(string qty) =>
        CodeOf(() => Calc(Header(), lines: Line(qty: decimal.Parse(qty, System.Globalization.CultureInfo.InvariantCulture))))
            .Should().Be(GoodsReceiptRules.QtyCode);

    [Fact, Trait("Id", "MM-UT-103"), Trait("Rule", "MM-BR-103")]
    public void Price_CannotBeZero_ButANegativePriceIsAllowed()
    {
        CodeOf(() => Calc(Header(), lines: Line(price: 0m))).Should().Be(GoodsReceiptRules.PriceCode);
        Calc(Header(), lines: Line(qty: 1m, price: -5m)).Totals.SupplyAmt.Should().Be(-5m);
    }

    // SD-BR-513: a Set's stock grows only by assembly, so a goods receipt line of a Set says so
    [Fact, Trait("Id", "SD-UT-513"), Trait("Rule", "SD-BR-513")]
    public void Item_ASet_IsSdE513()
    {
        var error = Assert.Throws<DomainException>(() => Calc(Header(), lines: [Line(), Line(item: SetItem)]));

        (error.Code, error.Args).Should().BeEquivalentTo((SetItemRules.PurchaseCode, new object[] { 2 }));
    }

    [Fact, Trait("Id", "MM-UT-115"), Trait("Rule", "MM-BR-115")]
    public void Item_MustBeInventoryOrService()
    {
        CodeOf(() => Calc(Header(), lines: Line(item: Asset))).Should().Be(GoodsReceiptRules.ItemCode);
        CodeOf(() => Calc(Header(), lines: Line(item: 99))).Should().Be(GoodsReceiptRules.ItemCode);
        CodeOf(() => Calc(Header(), lines: Line(item: Kit))).Should().Be(GoodsReceiptRules.ItemCode);
        Calc(Header(), lines: Line(item: Service, taxCode: NoTax)).Lines.Single().ItemId.Should().Be(Service);
    }

    [Fact, Trait("Id", "MM-UT-703"), Trait("Rule", "MM-BR-703")]
    public void Line_NeedsAWarehouseInUse()
    {
        CodeOf(() => Calc(Header(), lines: Line(warehouse: null))).Should().Be(GoodsReceiptRules.WarehouseCode);
        CodeOf(() => Calc(Header(), lines: Line(warehouse: 8))).Should().Be(GoodsReceiptRules.NotFoundCode);
    }

    [Fact, Trait("Id", "MM-UT-112"), Trait("Rule", "MM-BR-112"), Trait("Id", "MM-UT-162"), Trait("Rule", "MM-BR-162")]
    public void Totals_AreRoundedSupplyPlusTax_AndSavedLinesCountInTheTotals()
    {
        var (lines, totals) = Calc(Header(), kept: [Stored(qty: 2m, price: 50m)], lines: [Line(qty: 3m, price: 1.3333m)]);

        lines.Select(l => (l.Id, l.Seq, l.GrAmt, l.SupplyAmt, l.TaxAmt)).Should().Equal((101L, 1, 100m, 100m, 10m), (null, 2, 4m, 4m, 0.4m));
        totals.Should().Be(new GoodsReceiptTotals(104m, 10.4m, 114.4m, 114.4m));
    }

    [Fact, Trait("Id", "MM-UT-164"), Trait("Rule", "MM-BR-164"), Trait("Id", "MM-UT-163"), Trait("Rule", "MM-BR-163")]
    public void StockAmount_HasSalesTaxAndNonDeductibleVat_InBaseCurrency()
    {
        var (lines, _) = Calc(Header("USD", 1300m), KrwBase, lines:
        [
            Line(qty: 1m, price: 100m, taxCode: Vat10),
            Line(qty: 1m, price: 100m, taxCode: Vat10, deductible: false),
            Line(qty: 1m, price: 100m, taxCode: SalesTax8),
        ]);

        lines.Select(l => l.StockAmt).Should().Equal(130_000m, 143_000m, 140_400m);
        lines[0].StockPrice.Should().Be(130_000m);
    }

    [Fact, Trait("Id", "MM-UT-161"), Trait("Rule", "MM-BR-161")]
    public void Currency_ComesFromThePo_AndCannotDiffer()
    {
        GoodsReceiptRules.DocumentCurrency(null, null, [PoLine()]).Should().Be(("USD", 1m));
        GoodsReceiptRules.DocumentCurrency("KRW", 1m, []).Should().Be(("KRW", 1m));
        CodeOf(() => GoodsReceiptRules.DocumentCurrency("KRW", null, [PoLine()])).Should().Be(GoodsReceiptRules.CurrencyCode);
        CodeOf(() => GoodsReceiptRules.DocumentCurrency(null, 2m, [PoLine()])).Should().Be(GoodsReceiptRules.CurrencyCode);
        CodeOf(() => GoodsReceiptRules.DocumentCurrency(null, null, [PoLine(), PoLine() with { ExchangeRate = 2m }]))
            .Should().Be(GoodsReceiptRules.CurrencyCode);
        CodeOf(() => Calc(Header("USD", 2m))).Should().Be(GoodsReceiptRules.CurrencyCode);
    }

    [Theory, Trait("Id", "MM-UT-024"), Trait("Rule", "MM-BR-024"), Trait("Id", "MM-UT-113"), Trait("Rule", "MM-BR-113")]
    [InlineData(PoStatusCode.Draft, Vendor)]
    [InlineData(PoStatusCode.Received, Vendor)]
    [InlineData(PoStatusCode.Closed, Vendor)]
    [InlineData(PoStatusCode.Cancelled, Vendor)]
    [InlineData(PoStatusCode.Approved, 10L)]
    public void PoLines_OnlyFromPoOrReOrdersOfTheVendor(string status, long poVendor) =>
        CodeOf(() => Calc(Header(), masters: Masters(status, poVendor), lines: Line(poLine: PoLineId)))
            .Should().Be(GoodsReceiptRules.PoNotReceivableCode);

    [Theory, Trait("Id", "MM-UT-024"), Trait("Rule", "MM-BR-024")]
    [InlineData(PoStatusCode.Approved)]
    [InlineData(PoStatusCode.PartiallyReceived)]
    public void PoLines_FromPoAndRe_AreReceived(string status) =>
        Calc(Header(), masters: Masters(status), lines: Line(poLine: PoLineId)).Lines.Single().PurchaseOrderLineId.Should().Be(PoLineId);

    [Fact, Trait("Id", "MM-UT-102"), Trait("Rule", "MM-BR-102"), Trait("Id", "MM-UT-114"), Trait("Rule", "MM-BR-114")]
    public void OverReceipt_SumsTheNewLinesOfOnePoLine_AndIgnoresLinesWithoutAPo()
    {
        var masters = Masters(received: 4m);
        var (exact, _) = Calc(Header(), masters: masters, lines: [Line(qty: 6m, poLine: PoLineId), Line(qty: 99m)]);
        var (over, _) = Calc(Header(), masters: masters, lines: [Line(qty: 4m, poLine: PoLineId), Line(qty: 99m), Line(qty: 3m, poLine: PoLineId)]);

        GoodsReceiptRules.OverReceipts(exact, masters.PoLines).Should().BeEmpty();
        GoodsReceiptRules.OverReceipts(over, masters.PoLines).Should().Equal(new OverReceipt("PO2611000001", 1, 6m));
    }

    [Fact, Trait("Id", "MM-UT-102"), Trait("Rule", "MM-BR-102")]
    public void OverReceiptMode_DefaultsToDeny() =>
        new[] { OverReceiptModes.OrDefault(null), OverReceiptModes.OrDefault("WARN"), OverReceiptModes.OrDefault("X") }
            .Should().Equal("DENY", "WARN", "DENY");

    [Fact, Trait("Id", "MM-UT-104"), Trait("Rule", "MM-BR-104"), Trait("Id", "MM-UT-790"), Trait("Rule", "MM-BR-790")]
    public void Editable_OnlyWithoutInvoicedOrReturnedQty()
    {
        GoodsReceiptRules.EnsureEditable("GR1", [Stored(), Stored(2)]);
        CodeOf(() => GoodsReceiptRules.EnsureEditable("GR1", [Stored(), Stored(2, invoiced: 1m)])).Should().Be(GoodsReceiptRules.LockedCode);
        CodeOf(() => GoodsReceiptRules.EnsureEditable("GR1", [Stored(returned: 1m)])).Should().Be(GoodsReceiptRules.LockedCode);
    }

    [Fact, Trait("Id", "MM-UT-107"), Trait("Rule", "MM-BR-107")]
    public void SavedLine_CannotChange()
    {
        var stored = Stored();
        GoodsReceiptRules.EnsureUnchanged(stored, Line());
        GoodsReceiptRules.EnsureUnchanged(stored, Line(warehouse: null));
        CodeOf(() => GoodsReceiptRules.EnsureUnchanged(stored, Line(qty: 9m))).Should().Be(GoodsReceiptRules.LineChangedCode);
        CodeOf(() => GoodsReceiptRules.EnsureUnchanged(stored, Line(price: 99m))).Should().Be(GoodsReceiptRules.LineChangedCode);
        CodeOf(() => GoodsReceiptRules.EnsureUnchanged(stored, Line(taxCode: NoTax))).Should().Be(GoodsReceiptRules.LineChangedCode);
        CodeOf(() => GoodsReceiptRules.EnsureUnchanged(stored, Line(deductible: false))).Should().Be(GoodsReceiptRules.LineChangedCode);
    }

    [Fact, Trait("Id", "MM-UT-107"), Trait("Rule", "MM-BR-107"), Trait("Id", "MM-UT-364"), Trait("Rule", "MM-BR-364")]
    public void SavedLine_KeepsItsLocationAndPalletQty_ButItsLotAttributesMayChange()
    {
        var stored = Stored(palletQty: 5m);

        GoodsReceiptRules.EnsureUnchanged(stored, Line(palletQty: 5m, lot: new LotAttributes("S-1", GrDt, null)));
        GoodsReceiptRules.EnsureUnchanged(stored, Line(palletQty: 5m, location: 70));
        CodeOf(() => GoodsReceiptRules.EnsureUnchanged(stored, Line(palletQty: 5m, location: 71))).Should().Be(GoodsReceiptRules.LineChangedCode);
        CodeOf(() => GoodsReceiptRules.EnsureUnchanged(stored, Line(palletQty: 4m))).Should().Be(GoodsReceiptRules.LineChangedCode);
        CodeOf(() => GoodsReceiptRules.EnsureUnchanged(stored, Line())).Should().Be(GoodsReceiptRules.LineChangedCode);
    }

    [Fact, Trait("Id", "MM-UT-104"), Trait("Rule", "MM-BR-104"), Trait("Id", "MM-UT-364"), Trait("Rule", "MM-BR-364")]
    public void LockedReceipt_TakesOnlyLotAttributeChanges()
    {
        StoredGoodsReceiptLine[] lines = [Stored(invoiced: 1m), Stored(2)];
        static (long?, GoodsReceiptLineInput) Same(StoredGoodsReceiptLine l) =>
            (l.Id, Line(l.GrQty, l.GrPrc) with { Lot = new LotAttributes("NEW", null, null) });

        GoodsReceiptRules.EnsureLotAttributesOnly("GR1", Header(), Header(), lines, [.. lines.Select(Same)]);
        CodeOf(() => GoodsReceiptRules.EnsureLotAttributesOnly("GR1", Header(), Header() with { Remarks = "x" }, lines, [.. lines.Select(Same)]))
            .Should().Be(GoodsReceiptRules.LockedCode);
        CodeOf(() => GoodsReceiptRules.EnsureLotAttributesOnly("GR1", Header(), Header(), lines, [Same(lines[0])]))
            .Should().Be(GoodsReceiptRules.LockedCode, "a line is deleted");
        CodeOf(() => GoodsReceiptRules.EnsureLotAttributesOnly("GR1", Header(), Header(), lines, [.. lines.Select(Same), (null, Line())]))
            .Should().Be(GoodsReceiptRules.LockedCode, "a line is added");
        CodeOf(() => GoodsReceiptRules.EnsureLotAttributesOnly("GR1", Header(), Header(), lines, [Same(lines[0]), (lines[1].Id, Line(qty: 1m))]))
            .Should().Be(GoodsReceiptRules.LockedCode, "a line is changed");
    }

    [Fact, Trait("Id", "MM-UT-364"), Trait("Rule", "MM-BR-364")]
    public void LotAttributes_OnlyForALotManagedItem_WithASupplierLotOf30AndExpiryNotBeforeMfg()
    {
        var lot = new LotAttributes(" S-1 ", GrDt, GrDt);

        Calc(Header(), lines: Line(item: LotItem, lot: lot)).Lines.Single().Lot.Should().Be(new LotAttributes("S-1", GrDt, GrDt));
        Calc(Header(), lines: Line(lot: new LotAttributes(" ", null, null))).Lines.Single().Lot.Should().Be(LotAttributes.None);
        Calc(Header(), lines: Line(item: LotItem, lot: lot with { SupplierLotNo = new string('A', 30) })).Lines.Should().ContainSingle();
        var error = Assert.Throws<DomainException>(
            () => Calc(Header(), lines: [Line(), Line(item: LotItem, lot: lot with { SupplierLotNo = new string('A', 31) })]));
        (error.Code, error.Args).Should().BeEquivalentTo((LotRules.AttributesCode, new object[] { 2 }));
        CodeOf(() => Calc(Header(), lines: Line(item: LotItem, lot: lot with { ExpiryDt = GrDt.AddDays(-1) }))).Should().Be(LotRules.AttributesCode);
        CodeOf(() => Calc(Header(), lines: Line(lot: lot))).Should().Be(LotRules.AttributesCode);
        CodeOf(() => Calc(Header(), kept: [Stored(lot: lot)])).Should().Be(LotRules.AttributesCode, "a saved line is checked too");
    }

    [Fact, Trait("Id", "MM-UT-910"), Trait("Rule", "MM-BR-910")]
    public void PalletQty_AboveZero_AndOnlyForAStockItem()
    {
        Calc(Header(), lines: Line(palletQty: 4m)).Lines.Single().PalletQty.Should().Be(4m);
        CodeOf(() => Calc(Header(), lines: Line(palletQty: 0m))).Should().Be(PalletRules.QtyCode);
        CodeOf(() => Calc(Header(), lines: Line(palletQty: -1m))).Should().Be(PalletRules.QtyCode);
        CodeOf(() => Calc(Header(), lines: Line(item: Service, taxCode: NoTax, palletQty: 1m))).Should().Be(PalletRules.QtyCode);
    }

    [Theory, Trait("Id", "MM-UT-910"), Trait("Rule", "MM-BR-910")]
    [InlineData("25", "10", "10|10|5")]
    [InlineData("20", "10", "10|10")]
    [InlineData("3", "10", "3")]
    [InlineData("2.5", "1", "1|1|0.5")]
    public void Pallets_SplitTheQty_TheLastTakesTheRest(string qty, string palletQty, string expected) =>
        PalletRules.Split(Dec(qty), Dec(palletQty)).Should().Equal(expected.Split('|').Select(Dec));

    [Fact, Trait("Id", "MM-UT-911"), Trait("Rule", "MM-BR-911")]
    public void DeletingAReceiptLine_NeedsItsPalletsAsReceived()
    {
        PalletRules.EnsureAsReceived("PL2611000001", asReceived: true);
        var error = Assert.Throws<DomainException>(() => PalletRules.EnsureAsReceived("PL2611000002", asReceived: false));
        (error.Code, error.Args).Should().BeEquivalentTo((PalletRules.ChangedCode, new object[] { "PL2611000002" }));
    }

    private static decimal Dec(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    [Fact, Trait("Id", "MM-UT-116"), Trait("Rule", "MM-BR-116"), Trait("Id", "MM-UT-117"), Trait("Rule", "MM-BR-117")]
    public void SavedHeader_KeepsItsVendor_AndItsDateWhileItHasLines()
    {
        var stored = Header();
        var changed = Header() with { RefNo = "DN-2", Remarks = "late", WarehouseId = 8 };

        GoodsReceiptRules.EnsureHeaderChange(stored, 1, changed);
        GoodsReceiptRules.EnsureHeaderChange(stored, 0, changed with { GrDt = GrDt.AddDays(1) });
        CodeOf(() => GoodsReceiptRules.EnsureHeaderChange(stored, 0, changed with { VendorId = 10 })).Should().Be(GoodsReceiptRules.VendorChangedCode);
        CodeOf(() => GoodsReceiptRules.EnsureHeaderChange(stored, 1, changed with { GrDt = GrDt.AddDays(1) })).Should().Be(GoodsReceiptRules.DateChangedCode);
        CodeOf(() => GoodsReceiptRules.EnsureHeaderChange(stored, 1, changed with { CurrencyCode = "KRW" })).Should().Be(GoodsReceiptRules.CurrencyCode);
    }

    [Theory, Trait("Id", "MM-UT-105"), Trait("Rule", "MM-BR-105")]
    [InlineData(0, 0, 0, "VO")]
    [InlineData(100, 0, 0, "OP")]
    [InlineData(100, 4, 0, "PA")]
    [InlineData(100, 10, 0, "CL")]
    [InlineData(100, 6, 4, "CL")]
    [InlineData(100, 0, 10, "OP")]
    public void Status_IsComputedFromQuantities(int total, int invoiced, int returned, string expected) =>
        GoodsReceiptRules.StatusOf(total, [(10m, invoiced, returned), (5m, invoiced == 0 ? 0m : 5m, 0m)]).Should().Be(expected);

    [Theory, Trait("Id", "MM-UT-018"), Trait("Rule", "MM-BR-018"), Trait("Id", "MM-UT-109"), Trait("Rule", "MM-BR-109")]
    [InlineData(PoStatus.Approved, 0, PoStatus.Approved)]
    [InlineData(PoStatus.Approved, 4, PoStatus.PartiallyReceived)]
    [InlineData(PoStatus.PartiallyReceived, 15, PoStatus.Received)]
    [InlineData(PoStatus.Received, 4, PoStatus.PartiallyReceived)]
    [InlineData(PoStatus.Received, 0, PoStatus.Approved)]
    [InlineData(PoStatus.Approved, 20, PoStatus.Received)]
    [InlineData(PoStatus.Closed, 0, PoStatus.Closed)]
    [InlineData(PoStatus.Draft, 4, PoStatus.Draft)]
    public void PoStatus_AfterReceipt(PoStatus status, int receivedOnFirstLine, PoStatus expected) =>
        PoStatusRules.AfterReceipt(status, [(10m, receivedOnFirstLine), (5m, receivedOnFirstLine >= 10 ? 5m : 0m)]).Should().Be(expected);

    [Theory, Trait("Id", "MM-UT-920"), Trait("Rule", "MM-BR-920"), Trait("Id", "MM-UT-923"), Trait("Rule", "MM-BR-923")]
    [InlineData(false, "PO", null, "MM-E-923")]
    [InlineData(true, "DR", null, "MM-E-024")]
    [InlineData(true, "CL", null, "MM-E-024")]
    [InlineData(true, "MC", null, "MM-E-024")]
    [InlineData(true, "PO", 8L, "MM-E-024")]
    [InlineData(true, "PO", null, null)]
    [InlineData(true, "RE", Vendor, null)]
    [InlineData(true, null, null, null)]
    public void PdaPo_MustBeKnown_AndReceivable(bool found, string? status, long? vendorId, string? expected)
    {
        var act = () => GoodsReceiptRules.EnsurePdaPo("PO-1", found, status, Vendor, vendorId);

        if (expected is null)
        {
            act.Should().NotThrow();
        }
        else
        {
            var error = act.Should().Throw<DomainException>().Which;
            (error.Code, error.Args).Should().BeEquivalentTo((expected, new object[] { "PO-1" }));
        }
    }

    [Fact, Trait("Id", "MM-UT-923"), Trait("Rule", "MM-BR-923")]
    public void PdaItem_MustHaveARemainingLineOnThePo()
    {
        var onPo = () => GoodsReceiptRules.EnsureItemOnPo(LotItem, "LT-1", [Inventory, LotItem]);
        var notOnPo = () => GoodsReceiptRules.EnsureItemOnPo(Service, "SV-1", [Inventory, LotItem]);
        var unknown = () => GoodsReceiptRules.EnsureItemOnPo(99, null, [Inventory]);

        onPo.Should().NotThrow();
        notOnPo.Should().Throw<DomainException>().Which.Args.Should().Equal("SV-1");
        var error = unknown.Should().Throw<DomainException>().Which;
        (error.Code, error.Args).Should().BeEquivalentTo(("MM-E-923", new object[] { "99" }));
    }
}
