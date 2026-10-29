using FluentAssertions;
using QuroErp.Domain.Mm.Inventory;

namespace QuroErp.Domain.Tests.Mm.Inventory;

/// <summary>NegativeStockMode is stored as ALLOW, ALLOW_IN_MONTH or DENY (MM-BR-003).</summary>
public class NegativeStockModeTests
{
    [Theory, Trait("Id", "MM-SET-T01")]
    [InlineData(NegativeStockMode.Allow, "ALLOW")]
    [InlineData(NegativeStockMode.AllowInMonth, "ALLOW_IN_MONTH")]
    [InlineData(NegativeStockMode.Deny, "DENY")]
    public void ToCodeAndTryParse_RoundTrip(NegativeStockMode mode, string code)
    {
        mode.ToCode().Should().Be(code);
        NegativeStockModeCode.TryParse(code, out var parsed).Should().BeTrue();
        parsed.Should().Be(mode);
    }

    [Theory, Trait("Id", "MM-SET-T02")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("deny")]
    [InlineData("Allow")]
    public void TryParse_UnknownCode_ReturnsFalse(string? code)
    {
        NegativeStockModeCode.TryParse(code, out _).Should().BeFalse();
    }
}
