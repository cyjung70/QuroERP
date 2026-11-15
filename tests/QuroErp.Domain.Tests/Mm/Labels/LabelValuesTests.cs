using FluentAssertions;
using QuroErp.Domain.Mm.Labels;

namespace QuroErp.Domain.Tests.Mm.Labels;

/// <summary>The values and DataMatrix content of each label type (MM-BR-918).</summary>
public class LabelValuesTests
{
    private static readonly DateOnly Expiry = new(2027, 3, 31);

    [Fact, Trait("Id", "MM-UT-918"), Trait("Id", "MM-UC-037-T02"), Trait("Rule", "MM-BR-918")]
    public void Pallet_PrintsQtyWithoutTrailingZeros_AndItsCodeIsPlNumberItemLotQtyExpiry()
    {
        var label = LabelValues.Pallet(7, "PL2611000001", "BOLT", "Hex Bolt", "L1", 24.50m, Expiry);

        label.Code.Should().Be("PL|PL2611000001|BOLT|L1|24.5|20270331");
        label.Values.Should().BeEquivalentTo(new Dictionary<string, string?>
        {
            ["palletNo"] = "PL2611000001",
            ["itemCd"] = "BOLT",
            ["itemName"] = "Hex Bolt",
            ["lotNo"] = "L1",
            ["qty"] = "24.5",
            ["expiryDt"] = "2027-03-31",
        });
        (label.LabelType, label.RefId).Should().Be((LabelType.Pallet, 7L));
        LabelPages.For([label]).Select(p => (p.Text, p.Barcode)).Should().Equal((true, false), (false, true));
    }

    [Fact, Trait("Id", "MM-UT-918"), Trait("Rule", "MM-BR-918")]
    public void ItemLotAndLocation_HaveTheirCodes_AndPrintOnePage()
    {
        var item = LabelValues.Item(1, "BOLT", "Hex Bolt");
        var lot = LabelValues.Lot(2, "BOLT", "Hex Bolt", "L1", null);
        var location = LabelValues.Location(3, "MAIN", "A-01");

        (item.Code, lot.Code, location.Code).Should().Be(("IT|BOLT", "LT|BOLT|L1", "LC|MAIN|A-01"));
        lot.Values["expiryDt"].Should().BeNull();
        location.Values.Keys.Should().BeEquivalentTo(LabelFormatRules.FieldsOf(LabelType.Location));
        LabelPages.For([item, lot, location]).Should().OnlyContain(p => p.Text && p.Barcode).And.HaveCount(3);
    }
}
