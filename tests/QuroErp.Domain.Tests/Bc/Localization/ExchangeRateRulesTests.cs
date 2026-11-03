using System.Globalization;
using FluentAssertions;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Tests.Bc.Localization;

/// <summary>Exchange rate rules (BC-UC-024, SD-BR-906, SD-BR-907, SD-BR-908, SD-BR-909, BC-BR-412).</summary>
public class ExchangeRateRulesTests
{
    private static readonly DateOnly Oct1 = new(2026, 10, 1);
    private static readonly HashSet<string> InUse = ["USD", "KRW", "IDR"];

    private static decimal D(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    [Theory, Trait("Id", "SD-UT-907"), Trait("Rule", "SD-BR-907")]
    [InlineData(" krw ", "0.00075", "KRW")]
    [InlineData("IDR", "0.0000628931", "IDR")]
    [InlineData("KRW", "1350.5000000000000", "KRW")]
    public void Normalize_ACurrencyInUse_AndARateAbove0_IsKept(string code, string rate, string expected) =>
        ExchangeRateRules.Normalize(Oct1, code, D(rate), InUse, "USD").Should().Be(new ExchangeRateFields(Oct1, expected, D(rate)));

    [Theory, Trait("Id", "BC-UC-024-T04"), Trait("Id", "BC-UC-024-T05"), Trait("Id", "SD-UT-907"), Trait("Rule", "SD-BR-907")]
    [InlineData("USD", "1")]
    [InlineData("EUR", "1")]
    [InlineData("KR", "1")]
    [InlineData(null, "1")]
    [InlineData("KRW", "0")]
    [InlineData("KRW", "-0.5")]
    [InlineData("KRW", null)]
    [InlineData("KRW", "0.00074074074")]
    public void Normalize_BaseOrUnusedCurrency_OrARateNotAbove0OrOver10Decimals_IsSdE907(string? code, string? rate)
    {
        var act = () => ExchangeRateRules.Normalize(Oct1, code, rate is null ? null : D(rate), InUse, "USD");

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be("SD-E-907");
        error.Args.Should().Equal("2026-10-01", code?.Trim().ToUpperInvariant() ?? "");
    }

    [Fact, Trait("Id", "SD-UT-907"), Trait("Rule", "SD-BR-907")]
    public void Normalize_WithoutADate_IsSdE907_WithAnEmptyDate()
    {
        var act = () => ExchangeRateRules.Normalize(null, "KRW", 1m, InUse, "USD");

        act.Should().Throw<DomainException>().Which.Args.Should().Equal("", "KRW");
    }

    [Fact, Trait("Id", "BC-UC-024-T03"), Trait("Id", "SD-UT-906"), Trait("Rule", "SD-BR-906")]
    public void EnsureUnique_SameDateOtherCurrency_OrOtherDate_Passes()
    {
        var act = () => ExchangeRateRules.EnsureUnique([(Oct1, "KRW"), (Oct1, "IDR"), (Oct1.AddDays(1), "KRW")]);

        act.Should().NotThrow();
    }

    [Fact, Trait("Id", "BC-UC-024-T01"), Trait("Id", "SD-UT-906"), Trait("Rule", "SD-BR-906")]
    public void EnsureUnique_SameDateAndCurrencyTwice_IsSdE906_WithTheKey()
    {
        var act = () => ExchangeRateRules.EnsureUnique([(Oct1, "KRW"), (Oct1, "IDR"), (Oct1, "KRW")]);

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be("SD-E-906");
        error.Args.Should().Equal("2026-10-01", "KRW");
    }

    [Fact, Trait("Id", "SD-UT-908"), Trait("Rule", "SD-BR-908")]
    public void Period_DefaultsToTheYearUpToToday_AndKeepsWhatIsGiven()
    {
        var today = new DateOnly(2026, 10, 31);

        ExchangeRateRules.Period(null, null, today).Should().Be((new DateOnly(2025, 10, 31), today));
        ExchangeRateRules.Period(null, Oct1, today).Should().Be((new DateOnly(2025, 10, 1), Oct1));
        ExchangeRateRules.Period(Oct1, null, today).Should().Be((Oct1, today));
    }

    [Theory, Trait("Id", "SD-UT-909"), Trait("Rule", "SD-BR-909")]
    [InlineData("15900", "0.0000628931")]
    [InlineData("1350", "0.0007407407")]
    [InlineData("1", "1")]
    public void FromLegacy_IsTheInverse_RoundedTo10Decimals(string unitsPerUsd, string expected) =>
        ExchangeRateRules.FromLegacy(D(unitsPerUsd)).Should().Be(D(expected));

    [Theory, Trait("Id", "SD-UT-909"), Trait("Rule", "SD-BR-909")]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("-1")]
    public void FromLegacy_MissingOrNotAbove0_HasNoRate(string? unitsPerUsd) =>
        ExchangeRateRules.FromLegacy(unitsPerUsd is null ? null : D(unitsPerUsd)).Should().BeNull();

    [Theory, Trait("Id", "BC-UT-412"), Trait("Rule", "BC-BR-412")]
    [InlineData("USD", "0.00075", "1")]
    [InlineData("KRW", "0.00075", "0.00075")]
    [InlineData("KRW", null, null)]
    public void DefaultRate_IsOneForTheBaseCurrency_ElseTheStoredRate(string code, string? stored, string? expected) =>
        ExchangeRateRules.DefaultRate(code, "USD", stored is null ? null : D(stored)).Should().Be(expected is null ? null : D(expected));
}
