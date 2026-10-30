using FluentAssertions;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Bc.PurchasePrices;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Tests.Bc.PurchasePrices;

public class PurchasePriceRulesTests
{
    // US company: MM unit prices have 4 decimals
    private static readonly DecimalPolicy UsDecimals = new(
        "US", [new DecimalSetting("MM", 2, RoundingMode.HalfUp)], [new CountryDecimal("US", "MM", 4)], new Dictionary<string, string?>());

    private static DomainException ErrorOf(Action act) => act.Should().Throw<DomainException>().Which;

    [Theory, Trait("Id", "MM-UT-901")]
    [InlineData(" abc ", "ABC")]
    [InlineData("VP001", "VP001")]
    public void Group_IsTrimmedUpperCaseUpTo5(string input, string expected)
    {
        PurchasePriceRules.NormalizeGroup(input).Should().Be(expected);
    }

    [Fact, Trait("Id", "MM-UT-901")]
    public void Group_EmptyIsRequired_LongerThan5IsMmE901()
    {
        ErrorOf(() => PurchasePriceRules.NormalizeGroup("  ")).Code.Should().Be(PurchasePriceRules.RequiredCode);
        ErrorOf(() => PurchasePriceRules.NormalizeGroup("ABCDEF")).Code.Should().Be(PurchasePriceRules.KeyCode);
    }

    [Fact, Trait("Id", "MM-UT-901")]
    public void Unique_EachItemOnceInTheGroup()
    {
        var act = () => PurchasePriceRules.EnsureUnique("VP1", ["A", "B"]);
        act.Should().NotThrow();

        var error = ErrorOf(() => PurchasePriceRules.EnsureUnique("VP1", ["A", "B", "A"]));
        (error.Code, error.Args).Should().BeEquivalentTo((PurchasePriceRules.KeyCode, new object[] { "VP1", "A" }));
    }

    [Theory, Trait("Id", "MM-UT-902")]
    [InlineData("INVENTORY", "Y")]
    [InlineData("INVENTORY", null)]
    public void Item_InventoryInUse_IsAccepted(string type, string? validFlag)
    {
        var act = () => PurchasePriceRules.EnsureItem("A", new PurchasePriceItem(1, "A", type, validFlag));

        act.Should().NotThrow();
    }

    [Theory, Trait("Id", "MM-UT-902")]
    [InlineData("SERVICE", "Y")]
    [InlineData("SET", "Y")]
    [InlineData("INVENTORY", "N")]
    public void Item_NotInventoryOrNotInUse_IsMmE902(string type, string validFlag)
    {
        var error = ErrorOf(() => PurchasePriceRules.EnsureItem("A", new PurchasePriceItem(1, "A", type, validFlag)));

        (error.Code, error.Args).Should().BeEquivalentTo((PurchasePriceRules.ItemCode, new object[] { "A" }));
    }

    [Fact, Trait("Id", "MM-UT-902")]
    public void Item_Unknown_IsMmE902()
    {
        ErrorOf(() => PurchasePriceRules.EnsureItem("NOPE", null)).Code.Should().Be(PurchasePriceRules.ItemCode);
    }

    [Theory, Trait("Id", "MM-UT-903")]
    [InlineData("0")]
    [InlineData("-1")]
    public void Price_NotAboveZero_IsMmE903(string price)
    {
        var error = ErrorOf(() => PurchasePriceRules.EnsurePrice("A", decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture), UsDecimals));

        (error.Code, error.Args).Should().BeEquivalentTo((PurchasePriceRules.PriceCode, new object[] { "A" }));
    }

    [Fact, Trait("Id", "MM-UT-903"), Trait("Id", "BC-UT-429")]
    public void Price_UsesTheCompanyCountryMmDecimals()
    {
        var act = () => PurchasePriceRules.EnsurePrice("A", 12.3456m, UsDecimals);
        act.Should().NotThrow();

        ErrorOf(() => PurchasePriceRules.EnsurePrice("A", 12.34567m, UsDecimals)).Code.Should().Be(DecimalPolicy.TooManyDecimalsCode);
    }
}
