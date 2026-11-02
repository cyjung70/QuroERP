using FluentAssertions;
using QuroErp.Domain.Bc.Tax;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Tests.Bc.Tax;

/// <summary>US tax area rules (BC-BR-511, BC-BR-512).</summary>
public class TaxAreaRulesTests
{
    private static readonly DateOnly Jan1 = new(2026, 1, 1);
    private static readonly DateOnly Jul1 = new(2026, 7, 1);

    [Fact, Trait("Id", "BC-UT-511"), Trait("Id", "BC-UT-512"), Trait("Rule", "BC-BR-511"), Trait("Rule", "BC-BR-512")]
    public void Normalize_TrimsAndUpperCases_AndSortsTheLinesByStartDate()
    {
        var fields = TaxAreaRules.Normalize(" nyc ", " New York City ", " ny ", [new(Jul1, 8.875m), new(Jan1, 0m)]);

        fields.Should().BeEquivalentTo(new { TaxAreaCode = "NYC", TaxAreaName = "New York City", StateCode = "NY" });
        fields.Rates.Should().Equal(new TaxAreaRate(Jan1, 0m), new TaxAreaRate(Jul1, 8.875m));
    }

    [Theory, Trait("Id", "BC-UT-511"), Trait("Rule", "BC-BR-511")]
    [InlineData(null, "Name", "NY")]
    [InlineData("  ", "Name", "NY")]
    [InlineData("A23456789012345678901", "Name", "NY")]
    [InlineData("NYC", " ", "NY")]
    [InlineData("NYC", "Name", null)]
    public void Normalize_AMissingOrTooLongCode_OrNoNameOrState_ThrowsBcE511(string? code, string? name, string? state)
    {
        var act = () => TaxAreaRules.Normalize(code, name, state, []);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(TaxAreaRules.InvalidCode);
    }

    [Fact, Trait("Id", "BC-UT-511"), Trait("Rule", "BC-BR-511")]
    public void Normalize_ACodeOf20Characters_Passes_AndEnsureUnique_ThrowsBcE511OnlyWhenTaken()
    {
        var code = new string('A', TaxAreaRules.CodeMaxLength);

        TaxAreaRules.Normalize(code, "Name", "NY", []).TaxAreaCode.Should().Be(code);
        FluentActions.Invoking(() => TaxAreaRules.EnsureUnique(code, takenByAnother: false)).Should().NotThrow();
        FluentActions.Invoking(() => TaxAreaRules.EnsureUnique(code, takenByAnother: true))
            .Should().Throw<DomainException>().Which.Args.Should().Equal(code);
    }

    [Theory, Trait("Id", "BC-UT-512"), Trait("Rule", "BC-BR-512")]
    [InlineData(null, "8.5")]
    [InlineData("2026-07-01", null)]
    [InlineData("2026-07-01", "-0.0001")]
    [InlineData("2026-07-01", "100")]
    [InlineData("2026-07-01", "8.12345")]
    public void Normalize_ALineWithoutDateOrWithABadRate_ThrowsBcE512_WithTheCodeAndDate(string? from, string? rate)
    {
        var line = new TaxAreaRateInput(
            from is null ? null : DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture),
            rate is null ? null : decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture));

        var act = () => TaxAreaRules.Normalize("nyc", "Name", "NY", [line]);

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(TaxAreaRules.InvalidRateCode);
        error.Args.Should().Equal("NYC", from ?? string.Empty);
    }

    [Fact, Trait("Id", "BC-UT-512"), Trait("Id", "BC-UC-029-T01"), Trait("Rule", "BC-BR-512")]
    public void Normalize_TheSameStartDateTwice_ThrowsBcE512()
    {
        var act = () => TaxAreaRules.Normalize("NYC", "Name", "NY", [new(Jan1, 8m), new(Jan1, 9m)]);

        act.Should().Throw<DomainException>().Which.Args.Should().Equal("NYC", "2026-01-01");
    }

    [Fact, Trait("Id", "BC-UT-512"), Trait("Rule", "BC-BR-512")]
    public void Normalize_TheHighestRateWith4Decimals_Passes()
    {
        TaxAreaRules.Normalize("NYC", "Name", "NY", [new(Jan1, 99.9999m)]).Rates.Should().Equal(new TaxAreaRate(Jan1, 99.9999m));
    }
}
