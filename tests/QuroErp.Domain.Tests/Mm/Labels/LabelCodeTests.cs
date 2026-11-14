using FluentAssertions;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Inventory;
using QuroErp.Domain.Mm.Labels;

namespace QuroErp.Domain.Tests.Mm.Labels;

/// <summary>Own-format DataMatrix content, Pallet numbers and label page order (MM-BR-910, MM-BR-916, MM-BR-918, MM-BR-923).</summary>
public class LabelCodeTests
{
    [Fact, Trait("Id", "MM-UT-918"), Trait("Rule", "MM-BR-918")]
    public void Build_ThenParse_GivesBackEveryKindAndValue()
    {
        var pallet = LabelCode.ForPallet("PL2610000001", "A-100", "261010-001", 24.5m, new DateOnly(2027, 3, 31));
        var noExpiry = LabelCode.ForPallet("PL2610000002", "A-100", "*", 10m, null);

        pallet.Should().Be("PL|PL2610000001|A-100|261010-001|24.5|20270331");
        LabelCode.Parse(pallet).Should().BeEquivalentTo(new ScanValue("PL", ["PL2610000001", "A-100", "261010-001", "24.5", "20270331"]));
        LabelCode.Parse(noExpiry).Parts.Should().Equal("PL2610000002", "A-100", "*", "10", "");
        LabelCode.Parse(LabelCode.ForItem("A-100")).Should().BeEquivalentTo(new ScanValue("IT", ["A-100"]));
        LabelCode.Parse(LabelCode.ForLot("A-100", "B1")).Should().BeEquivalentTo(new ScanValue("LT", ["A-100", "B1"]));
        LabelCode.Parse(LabelCode.ForLocation("MAIN", "A01")).Should().BeEquivalentTo(new ScanValue("LC", ["MAIN", "A01"]));
    }

    [Fact, Trait("Id", "MM-UT-923"), Trait("Rule", "MM-BR-923")]
    public void Parse_AValueWithoutAKind_IsPlain_ForTheItemBarcodeThenItemCodeLookup()
    {
        LabelCode.Parse(" 8801234567890 ").Should().BeEquivalentTo(new ScanValue(LabelCode.Plain, ["8801234567890"]));
    }

    [Theory, Trait("Id", "MM-UT-923"), Trait("Rule", "MM-BR-923")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("XX|A")]
    [InlineData("IT|")]
    [InlineData("IT|A|B")]
    [InlineData("LT|A")]
    [InlineData("LC|MAIN|")]
    [InlineData("PL|PL1|A|B|ten|")]
    [InlineData("PL|PL1|A|B|1|2027-03-31")]
    [InlineData("PL||A|B|1|")]
    public void Parse_UnknownKindOrWrongValues_ThrowsMmE923(string scanned)
    {
        var act = () => LabelCode.Parse(scanned);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(LabelCode.UnknownCode);
    }

    [Fact, Trait("Id", "MM-UT-918"), Trait("Rule", "MM-BR-918")]
    public void Build_AValueWithTheSeparator_IsRefused()
    {
        var act = () => LabelCode.ForItem("A|B");

        act.Should().Throw<ArgumentException>();
    }

    [Fact, Trait("Id", "MM-UT-918"), Trait("Rule", "MM-BR-918")]
    public void Pages_APalletPrintsItsLabelThenItsDataMatrix_OtherLabelsOnePage()
    {
        var p1 = new Label(LabelType.Pallet, 1, new Dictionary<string, string?>(), "PL|1");
        var p2 = new Label(LabelType.Pallet, 2, new Dictionary<string, string?>(), "PL|2");
        var lot = new Label(LabelType.Lot, 9, new Dictionary<string, string?>(), "LT|A|B");

        LabelPages.For([p1, p2]).Select(p => (p.Label.RefId, p.Text, p.Barcode)).Should().Equal(
            (1L, true, false), (1L, false, true), (2L, true, false), (2L, false, true));
        LabelPages.For([lot]).Select(p => (p.Text, p.Barcode)).Should().Equal((true, true));
    }

    [Theory, Trait("Id", "MM-UT-910"), Trait("Rule", "MM-BR-910")]
    [InlineData(null, "PL2610000001")]
    [InlineData("PL2610000041", "PL2610000042")]
    [InlineData("PL2609000041", "PL2610000001")]
    public void PalletNumber_IsPlYymmSixDigits_RestartingEveryMonth(string? last, string expected)
    {
        DocumentNumberFormat.Pallet.Next(new DateOnly(2026, 10, 10), last).Should().Be(expected);
    }

    [Fact, Trait("Id", "MM-UT-916"), Trait("Rule", "MM-BR-916")]
    public void PalletStock_ABalanceBelowThePalletQty_ThrowsMmE916()
    {
        PalletRules.EnsurePalletsCovered("A", "MAIN", "A01", "B1", 5, 5);
        var act = () => PalletRules.EnsurePalletsCovered("A", "MAIN", "A01", "B1", 4, 5);

        act.Should().Throw<DomainException>().Which.Args.Should().Equal("A", "MAIN", "A01", "B1", 4m, 5m);
    }
}
