using FluentAssertions;
using QuroErp.Domain.Bc.Items;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Tests.Bc.Items;

/// <summary>Item barcode generation rules (MM-BR-815~817).</summary>
public class ItemBarcodeTests
{
    [Fact, Trait("Id", "MM-UT-815"), Trait("Id", "BC-UC-011-T01")]
    public void Generate_StartsAtStartNoPlusOne_InTheGivenOrder()
    {
        ItemBarcode.Generate("1000000000", 3).Should().Equal("1000000001", "1000000002", "1000000003");
        ItemBarcode.Generate(" 0099 ", 2).Should().Equal("100", "101");
        ItemBarcode.Generate("5", 0).Should().BeEmpty();
    }

    [Theory, Trait("Id", "MM-UT-816"), Trait("Id", "BC-UC-011-T03")]
    [InlineData("9999999998", 2)]
    [InlineData("10000000000", 1)]
    [InlineData("12A", 1)]
    [InlineData("-1", 1)]
    [InlineData("１２", 1)]
    [InlineData("", 1)]
    [InlineData(null, 1)]
    public void Generate_NotDigitsOrOverTenDigits_IsMmE816(string? startNo, int count)
    {
        var generate = () => ItemBarcode.Generate(startNo, count);

        generate.Should().Throw<DomainException>().Which.Should().BeEquivalentTo(new { Code = "MM-E-816", Args = new object[] { 10 } });
    }

    [Fact, Trait("Id", "MM-UT-816")]
    public void Generate_LastBarcodeOfTenDigits_IsAllowed() =>
        ItemBarcode.Generate("9999999998", 1).Should().Equal("9999999999");

    [Fact, Trait("Id", "MM-UT-817")]
    public void Duplicate_NamesTheOtherItemsOnceInCodeOrder() =>
        ItemBarcode.Duplicate(["B200", "A100", "B200"]).Should().BeEquivalentTo(new { Code = "MM-E-817", Args = new object[] { "A100, B200" } });
}
