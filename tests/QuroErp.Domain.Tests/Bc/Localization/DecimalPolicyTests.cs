using FluentAssertions;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Tests.Bc.Localization;

public class DecimalPolicyTests
{
    private static readonly DecimalPolicy Us = new(new UsCountryProfile().DefaultDecimalSettings);

    [Fact, Trait("Id", "BC-UT-423")]
    public void EnsureUnitPrice_WithinModuleDecimals_Passes()
    {
        var act = () =>
        {
            Us.EnsureUnitPrice(DecimalModule.Mm, 1.2345m, "unitPrice");
            Us.EnsureUnitPrice(DecimalModule.Sd, 1.20m, "unitPrice");
            Us.EnsureQty(DecimalModule.Mm, 3.25m, "qty");
            Us.EnsureQty(DecimalModule.Sd, 3.00m, "qty");
        };

        act.Should().NotThrow();
    }

    [Fact, Trait("Id", "BC-UT-423")]
    public void EnsureUnitPrice_TooManyDecimals_RejectsWithFieldAndDecimals()
    {
        var act = () => Us.EnsureUnitPrice(DecimalModule.Sd, 1.234m, "unitPrice");

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(DecimalPolicy.TooManyDecimalsCode);
        error.Args.Should().Equal("unitPrice", 2);
    }

    [Fact, Trait("Id", "BC-UT-423")]
    public void EnsureQty_FractionWhenSdQtyHasNoDecimals_RejectsBcE423()
    {
        var act = () => Us.EnsureQty(DecimalModule.Sd, 1.5m, "qty");

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DecimalPolicy.TooManyDecimalsCode);
    }

    [Fact, Trait("Id", "BC-UT-424")]
    public void RoundUnitPrice_ComputedValue_UsesModuleDecimalsAndMode()
    {
        var policy = new DecimalPolicy(
        [
            new DecimalSetting(DecimalModule.Mm, 4, 2, RoundingMode.HalfUp),
            new DecimalSetting(DecimalModule.Sd, 0, 0, RoundingMode.Down),
        ]);

        policy.RoundUnitPrice(DecimalModule.Mm, 1.23455m).Should().Be(1.2346m);
        policy.RoundQty(DecimalModule.Mm, 2.005m).Should().Be(2.01m);
        policy.RoundUnitPrice(DecimalModule.Sd, 1999.9m).Should().Be(1999m);
        policy.RoundQty(DecimalModule.Sd, 3.7m).Should().Be(3m);
    }

    [Fact, Trait("Id", "BC-UT-422")]
    public void SettingOf_ModuleWithoutSetting_ThrowsBcE422()
    {
        var act = () => new DecimalPolicy([]).SettingOf(DecimalModule.Mm);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DecimalSetting.InvalidCode);
    }

    [Theory, Trait("Id", "BC-UT-422")]
    [InlineData("MM", 7, 2)]
    [InlineData("MM", -1, 2)]
    [InlineData("SD", 2, 5)]
    [InlineData("SD", 2, -1)]
    [InlineData("FI", 2, 2)]
    public void DecimalSetting_OutOfRangeOrUnknownModule_ThrowsBcE422(string module, int unitPriceDecimals, int qtyDecimals)
    {
        var act = () => new DecimalSetting(module, unitPriceDecimals, qtyDecimals, RoundingMode.HalfUp);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DecimalSetting.InvalidCode);
    }

    [Theory, Trait("Id", "BC-UT-422")]
    [InlineData(6, 4)]
    [InlineData(0, 0)]
    public void DecimalSetting_Bounds_AreAllowed(int unitPriceDecimals, int qtyDecimals)
    {
        var act = () => new DecimalSetting(DecimalModule.Mm, unitPriceDecimals, qtyDecimals, RoundingMode.Down);

        act.Should().NotThrow();
    }
}
