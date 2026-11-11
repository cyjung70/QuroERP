using FluentAssertions;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Bc.SalesPrices;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Tests.Bc.SalesPrices;

/// <summary>Sales price table rules (BC-UC-014, SD-BR-403, SD-BR-407~409).</summary>
public class SalesPriceRulesTests
{
    // US company: SD unit prices have 4 decimals
    private static readonly DecimalPolicy UsDecimals = new(
        "US", [new DecimalSetting("SD", 2, RoundingMode.HalfUp)], [new CountryDecimal("US", "SD", 4)], new Dictionary<string, string?>());

    private static DomainException ErrorOf(Action act) => act.Should().Throw<DomainException>().Which;

    [Theory, Trait("Id", "SD-UT-409")]
    [InlineData(" abc ", "ABC")]
    [InlineData("PL001", "PL001")]
    [InlineData("ABCDEF", "ABCDEF")]
    [InlineData(" Blue River Market ", "BLUE RIVER MARKET")]
    public void Level_IsTrimmedUpperCase(string input, string expected)
    {
        SalesPriceRules.NormalizeLevel(input).Should().Be(expected);
    }

    [Fact, Trait("Id", "SD-UT-409")]
    public void Level_IsTheCustomerPriceLevelLength()
    {
        SalesPriceRules.LevelMaxLength.Should().Be(100);
        SalesPriceRules.NormalizeLevel(new string('A', 100)).Should().HaveLength(100);
    }

    [Fact, Trait("Id", "SD-UT-409")]
    public void Level_EmptyIsRequired_LongerThan100IsSdE403()
    {
        ErrorOf(() => SalesPriceRules.NormalizeLevel("  ")).Code.Should().Be(SalesPriceRules.RequiredCode);
        ErrorOf(() => SalesPriceRules.NormalizeLevel(null)).Code.Should().Be(SalesPriceRules.RequiredCode);
        ErrorOf(() => SalesPriceRules.NormalizeLevel(new string('A', 101))).Code.Should().Be(SalesPriceRules.KeyCode);
    }

    [Fact, Trait("Id", "SD-UT-403")]
    public void Unique_EachItemOnceInTheLevel()
    {
        var act = () => SalesPriceRules.EnsureUnique("PL1", ["A", "B"]);
        act.Should().NotThrow();

        var error = ErrorOf(() => SalesPriceRules.EnsureUnique("PL1", ["A", "B", "A"]));
        (error.Code, error.Args).Should().BeEquivalentTo((SalesPriceRules.KeyCode, new object[] { "PL1", "A" }));
    }

    [Theory, Trait("Id", "SD-UT-408")]
    [InlineData("INVENTORY", "Y")]
    [InlineData("INVENTORY", null)]
    public void Item_InventoryInUse_IsAccepted(string type, string? validFlag)
    {
        var act = () => SalesPriceRules.EnsureItem("A", new SalesPriceItem(1, "A", type, validFlag));

        act.Should().NotThrow();
    }

    [Theory, Trait("Id", "SD-UT-408")]
    [InlineData("SERVICE", "Y")]
    [InlineData("SET", "Y")]
    [InlineData("INVENTORY", "N")]
    public void Item_NotInventoryOrNotInUse_IsSdE408(string type, string validFlag)
    {
        var error = ErrorOf(() => SalesPriceRules.EnsureItem("A", new SalesPriceItem(1, "A", type, validFlag)));

        (error.Code, error.Args).Should().BeEquivalentTo((SalesPriceRules.ItemCode, new object[] { "A" }));
    }

    [Fact, Trait("Id", "SD-UT-408")]
    public void Item_Unknown_IsSdE408()
    {
        ErrorOf(() => SalesPriceRules.EnsureItem("NOPE", null)).Code.Should().Be(SalesPriceRules.ItemCode);
    }

    [Theory, Trait("Id", "SD-UT-407")]
    [InlineData("0")]
    [InlineData("-1")]
    public void Price_NotAboveZero_IsSdE407(string price)
    {
        var error = ErrorOf(() => SalesPriceRules.EnsurePrice("A", decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture), UsDecimals));

        (error.Code, error.Args).Should().BeEquivalentTo((SalesPriceRules.PriceCode, new object[] { "A" }));
    }

    [Fact, Trait("Id", "SD-UT-407"), Trait("Id", "BC-UT-429")]
    public void Price_UsesTheCompanyCountrySdDecimals()
    {
        var act = () => SalesPriceRules.EnsurePrice("A", 12.3456m, UsDecimals);
        act.Should().NotThrow();

        ErrorOf(() => SalesPriceRules.EnsurePrice("A", 12.34567m, UsDecimals)).Code.Should().Be(DecimalPolicy.TooManyDecimalsCode);
    }
}
