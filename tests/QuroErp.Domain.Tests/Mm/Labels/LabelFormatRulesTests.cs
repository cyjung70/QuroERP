using FluentAssertions;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Labels;

namespace QuroErp.Domain.Tests.Mm.Labels;

/// <summary>Label format rules (MM-BR-917) and the sample label of the preview (MM-BR-918).</summary>
public class LabelFormatRulesTests
{
    private static readonly string[] PalletFields = ["qty", "palletNo"];
    private static readonly string[] PalletFieldsTrimmed = [" qty ", "palletNo"];

    private static LabelFormat Pallet(string outputKind = "zpl", decimal width = 100, decimal height = 150, int dpi = 203, string[]? fields = null) =>
        new(LabelType.Pallet, outputKind, width, height, dpi, fields ?? PalletFieldsTrimmed);

    [Fact, Trait("Id", "MM-UT-917"), Trait("Rule", "MM-BR-917")]
    public void Validate_AValidFormat_UppercasesTheOutputKind_TrimsAndKeepsTheFieldOrder()
    {
        var format = LabelFormatRules.Validate(Pallet(dpi: 300));

        format.OutputKind.Should().Be(LabelType.Zpl);
        format.Fields.Should().Equal(PalletFields);
        format.Dpi.Should().Be(300);
    }

    public static TheoryData<LabelFormat> Invalid() =>
    [
        Pallet(width: 0),
        Pallet(height: -1),
        Pallet(outputKind: "PNG"),
        Pallet(dpi: 600),
        Pallet(fields: []),
        Pallet(fields: ["warehouseCode"]),
        Pallet(fields: ["qty", "qty"]),
        new LabelFormat("BOX", LabelType.Pdf, 50, 25, 203, ["itemCd"]),
        new LabelFormat(LabelType.Location, LabelType.Pdf, 50, 25, 203, ["itemCd"]),
    ];

    [Theory, Trait("Id", "MM-UT-917"), Trait("Rule", "MM-BR-917")]
    [MemberData(nameof(Invalid))]
    public void Validate_AnInvalidFormat_ThrowsMmE917WithTheLabelType(LabelFormat format)
    {
        var act = () => LabelFormatRules.Validate(format);

        act.Should().Throw<DomainException>().Where(e => e.Code == LabelFormatRules.InvalidCode && e.Args.Single().Equals(format.LabelType));
    }

    [Fact, Trait("Id", "MM-UT-917"), Trait("Rule", "MM-BR-917")]
    public void Types_AreTheFourLabelTypes_AndEachHasItsFields()
    {
        LabelFormatRules.Types.Should().Equal(LabelType.Pallet, LabelType.Item, LabelType.Lot, LabelType.Location);
        LabelFormatRules.FieldsOf(LabelType.Item).Should().Equal(LabelField.ItemCd, LabelField.ItemName);
        LabelFormatRules.FieldsOf(LabelType.Location).Should().Equal(LabelField.WarehouseCode, LabelField.LocationCode);
        LabelFormatRules.IsType("BOX").Should().BeFalse();
    }

    [Theory, Trait("Id", "MM-UT-918"), Trait("Rule", "MM-BR-918")]
    [InlineData(LabelType.Pallet, "PL|PL2611000001|ITEM-001|261101-001|10|20271231", 2)]
    [InlineData(LabelType.Item, "IT|ITEM-001", 1)]
    [InlineData(LabelType.Lot, "LT|ITEM-001|261101-001", 1)]
    [InlineData(LabelType.Location, "LC|MAIN|A-01-01", 1)]
    public void Sample_HasTheTypeCode_OnlyTheTypeFields_AndThePalletPrintsTwoPages(string labelType, string code, int pages)
    {
        var sample = LabelFormatRules.Sample(labelType);

        sample.Code.Should().Be(code);
        LabelCode.Parse(sample.Code).Kind.Should().Be(code[..2]);
        sample.Values.Keys.Should().BeEquivalentTo(LabelFormatRules.FieldsOf(labelType));
        LabelPages.For([sample]).Should().HaveCount(pages);
    }
}
