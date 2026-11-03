using System.Data;
using FluentAssertions;
using QuroErp.Application.Bc.Tax;
using QuroErp.Domain.Bc.Tax;
using QuroErp.Domain.Common;

namespace QuroErp.Application.Tests.Bc.Tax;

public class TaxAreaRateProviderTests
{
    private static readonly DateOnly InvoiceDt = new(2026, 10, 30);

    private readonly TaxAreaRateProvider _provider = new(new FakeTaxRepository());

    [Theory, Trait("Id", "BC-UT-514")]
    [InlineData(TaxKind.VatStandard, "10", null, "10")]
    [InlineData(TaxKind.SalesTax, "0", 7L, "9.5")]
    [InlineData(TaxKind.SalesTaxExempt, "0", null, "0")]
    [InlineData(TaxKind.VatZero, "0", null, "0")]
    [InlineData(TaxKind.VatExempt, "0", null, "0")]
    [InlineData(TaxKind.None, "0", null, "0")]
    public async Task Rate_IsTheCodeRate_TheTaxAreaRateOnTheDate_OrZero(string kind, string codeRate, long? areaId, string expected)
    {
        var code = new TaxCodeRow(5, "T1", kind, decimal.Parse(codeRate, System.Globalization.CultureInfo.InvariantCulture), areaId, true, true);

        var rate = await _provider.GetRateAsync(code, InvoiceDt, null!, CancellationToken.None);

        rate.Should().Be(new TaxCodeRate(5, decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), true));
    }

    [Theory, Trait("Id", "BC-UT-513"), Trait("Id", "BC-UC-029-T03")]
    [InlineData(8L)]
    [InlineData(null)]
    public async Task SalesTax_WithoutARateOnTheDate_IsBcE513(long? areaId)
    {
        var code = new TaxCodeRow(6, "LA", TaxKind.SalesTax, 0m, areaId, false, true);

        var act = () => _provider.GetRateAsync(code, InvoiceDt, null!, CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal("LA", "2026-10-30");
    }

    /// <summary>Tax area 7 has 9.25% from 2026-01-01 and 9.5% from 2026-10-01; area 8 starts after the invoice date.</summary>
    private sealed class FakeTaxRepository : ITaxRepository
    {
        public Task<IReadOnlyList<TaxCodeRow>> FindTaxCodesAsync(
            IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<TaxAreaRateRow>> FindTaxAreaRatesAsync(
            long taxAreaId, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TaxAreaRateRow>>(taxAreaId switch
            {
                7 => [new(new DateOnly(2026, 1, 1), 9.25m), new(new DateOnly(2026, 10, 1), 9.5m)],
                8 => [new(new DateOnly(2026, 11, 1), 8m)],
                _ => [],
            });

        public Task<IReadOnlyList<TaxExemptionRow>> FindExemptionsAsync(
            long customerId, string stateCode, IDbTransaction transaction, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
