using System.Data;
using FluentAssertions;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Common;
using QuroErp.Application.Tests.Common;
using QuroErp.Application.Tests.Common.Auth;
using QuroErp.Contracts.Bc.Localization;
using QuroErp.Contracts.Common;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Common;

namespace QuroErp.Application.Tests.Bc.Localization;

/// <summary>Exchange rate management (BC-UC-024, SD-BR-905, SD-BR-906, SD-BR-907, SD-BR-908, BC-BR-412) with fake storage.</summary>
public class ExchangeRateServiceTests
{
    private static readonly DateTimeOffset Read = new(2026, 10, 30, 0, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly DateOnly Oct1 = new(2026, 10, 1);
    private static readonly DateOnly Oct2 = new(2026, 10, 2);

    private readonly FakeExchangeRateRepository _rates = new();
    private readonly FakeLocalizationRepository _localization = new() { Company = new("US", "USD", "en", "DOCUMENT", "America/New_York") };
    private readonly FakeUnitOfWorkFactory _uow = new();
    private readonly FakeBusinessClock _clock = new() { TodayValue = new(2026, 10, 31) };
    private readonly ExchangeRateService _service;

    public ExchangeRateServiceTests()
    {
        _service = new ExchangeRateService(_rates, new InUseCurrencies(), _localization, _clock, _uow);
        _rates.Add(1, Oct1, "KRW", 0.00075m);
        _rates.Add(2, Oct1, "IDR", 0.0000628931m);
        _rates.Add(3, Oct2, "KRW", 0.00074m);
        _rates.Add(4, new DateOnly(2025, 10, 30), "KRW", 0.0008m);
    }

    private static BatchRequest<ExchangeRateSaveRow, ExchangeRateKey> Batch(
        ExchangeRateSaveRow[]? added = null, ExchangeRateSaveRow[]? updated = null, ExchangeRateKey[]? deleted = null) =>
        new(added ?? [], updated ?? [], deleted ?? []);

    [Fact, Trait("Id", "SD-UT-908"), Trait("Rule", "SD-BR-908")]
    public async Task Search_DefaultsToTheYearUpToToday_NewestFirst_ThenCurrency()
    {
        var all = await _service.SearchAsync(null, null, null, None);
        var krw = await _service.SearchAsync(new DateOnly(2025, 1, 1), null, " krw ", None);

        all.Select(r => (r.RateDt, r.CurrencyCode)).Should().Equal((Oct2, "KRW"), (Oct1, "IDR"), (Oct1, "KRW"));
        krw.Select(r => r.Id).Should().Equal(3, 1, 4);
    }

    [Fact, Trait("Id", "BC-UC-024-T03"), Trait("Id", "BC-UC-024-T06"), Trait("Id", "SD-UT-905"), Trait("Rule", "SD-BR-905")]
    public async Task Save_AddsChangesAndDeletes_ThenCommitsOnce()
    {
        var after = await _service.SaveAsync(Batch(
            added: [new ExchangeRateSaveRow(null, Oct2, "idr", 0.000063m, null)],
            updated: [new ExchangeRateSaveRow(1, Oct1, "KRW", 0.000751m, Read)],
            deleted: [new ExchangeRateKey(3, Read)]), null, null, null, "kim", None);

        after.Select(r => (r.RateDt, r.CurrencyCode, r.Rate)).Should().Equal(
            (Oct2, "IDR", 0.000063m), (Oct1, "IDR", 0.0000628931m), (Oct1, "KRW", 0.000751m));
        _uow.Commits.Should().Be(1);
        _rates.Locks.Should().Be(1);
    }

    [Fact, Trait("Id", "SD-UT-906"), Trait("Rule", "SD-BR-906")]
    public async Task Save_SwapsTheDatesOfTwoRows_AndAReusedDeletedKey()
    {
        var after = await _service.SaveAsync(Batch(
            added: [new ExchangeRateSaveRow(null, Oct1, "IDR", 0.00006m, null)],
            updated: [new ExchangeRateSaveRow(1, Oct2, "KRW", 0.00075m, Read), new ExchangeRateSaveRow(3, Oct1, "KRW", 0.00074m, Read)],
            deleted: [new ExchangeRateKey(2, Read)]), null, null, null, "kim", None);

        after.Select(r => (r.Id, r.RateDt)).Should().Contain([(1L, Oct2), (3L, Oct1)]);
        after.Should().ContainSingle(r => r.CurrencyCode == "IDR").Which.Rate.Should().Be(0.00006m);
    }

    [Fact, Trait("Id", "BC-UC-024-T01"), Trait("Id", "SD-UT-906"), Trait("Rule", "SD-BR-906")]
    public async Task Save_AddingAStoredDateAndCurrency_IsSdE906_AndSavesNothing()
    {
        var act = () => _service.SaveAsync(Batch(
            added: [new ExchangeRateSaveRow(null, Oct2, "IDR", 0.00006m, null), new ExchangeRateSaveRow(null, Oct1, "KRW", 0.0007m, null)]),
            null, null, null, "kim", None);

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be("SD-E-906");
        error.Args.Should().Equal("2026-10-01", "KRW");
        _rates.Writes.Should().Be(0);
        _uow.Commits.Should().Be(0);
    }

    [Fact, Trait("Id", "BC-UC-024-T02"), Trait("Id", "SD-UT-906"), Trait("Rule", "SD-BR-906")]
    public async Task Save_ChangingARowOntoAStoredDateAndCurrency_IsSdE906()
    {
        var act = () => _service.SaveAsync(Batch(updated: [new ExchangeRateSaveRow(3, Oct1, "KRW", 0.00074m, Read)]), null, null, null, "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("SD-E-906");
        _rates.Writes.Should().Be(0);
    }

    [Theory, Trait("Id", "BC-UC-024-T04"), Trait("Id", "BC-UC-024-T05"), Trait("Id", "SD-UT-907"), Trait("Rule", "SD-BR-907")]
    [InlineData("USD", "1")]
    [InlineData("EUR", "1")]
    [InlineData("KRW", "0")]
    [InlineData("KRW", "-1")]
    public async Task Save_BaseOrUnusedCurrency_OrARateNotAbove0_IsSdE907_AndSavesNothing(string code, string rate)
    {
        var act = () => _service.SaveAsync(Batch(
            added: [new ExchangeRateSaveRow(null, Oct2, "IDR", 0.00006m, null), new ExchangeRateSaveRow(null, Oct2, code, decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture), null)]),
            null, null, null, "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("SD-E-907");
        _rates.Writes.Should().Be(0);
    }

    [Fact, Trait("Id", "SD-UT-905"), Trait("Rule", "SD-BR-905")]
    public async Task Save_ARowChangedOrDeletedAfterItWasRead_IsAConflict()
    {
        var stale = () => _service.SaveAsync(Batch(updated: [new ExchangeRateSaveRow(1, Oct1, "KRW", 0.0007m, Read.AddSeconds(-1))]), null, null, null, "kim", None);
        var gone = () => _service.SaveAsync(Batch(deleted: [new ExchangeRateKey(99, Read)]), null, null, null, "kim", None);

        await stale.Should().ThrowAsync<ConcurrencyException>();
        await gone.Should().ThrowAsync<ConcurrencyException>();
        _rates.Writes.Should().Be(0);
    }

    [Theory, Trait("Id", "BC-UT-412"), Trait("Rule", "BC-BR-412")]
    [InlineData("USD", 2026, 10, 2, "1")]
    [InlineData("krw", 2026, 10, 2, "0.00074")]
    [InlineData("KRW", 2026, 10, 15, "0.00074")]
    [InlineData("KRW", 2026, 9, 30, "0.0008")]
    [InlineData("KRW", 2025, 1, 1, null)]
    [InlineData("IDR", 2026, 9, 30, null)]
    public async Task DefaultRate_IsOneForTheBase_ElseTheLatestOnOrBefore_ElseNone(string code, int y, int m, int d, string? expected)
    {
        var rate = await _service.DefaultRateAsync(code, new DateOnly(y, m, d), null!, None);

        rate.Should().Be(expected is null ? null : decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>KRW and IDR in use, EUR not; USD is the company base currency. Only FindAllAsync is used.</summary>
    private sealed class InUseCurrencies : ICurrencyRepository
    {
        public Task<IReadOnlyList<CurrencyRecord>> FindAllAsync(bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CurrencyRecord>>(
            [
                new(1, "EUR", 2, null, false, Read), new(2, "IDR", 0, "ID", true, Read), new(3, "KRW", 0, "KR", true, Read),
                new(4, "USD", 2, "US", true, Read),
            ]);

        public Task<IReadOnlySet<string>> FindUsedByDocumentsAsync(IReadOnlyCollection<string> currencyCodes, IDbTransaction transaction, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlySet<string>> FindUsedByPartnersAsync(IReadOnlyCollection<string> currencyCodes, IDbTransaction transaction, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlySet<string>> FindUsedByExchangeRatesAsync(IReadOnlyCollection<string> currencyCodes, IDbTransaction transaction, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<long> InsertAsync(string currencyCode, CurrencyFields fields, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> UpdateAsync(long id, CurrencyFields fields, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SaveNamesAsync(long currencyId, IReadOnlyDictionary<string, string?> names, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeExchangeRateRepository : IExchangeRateRepository
    {
        private readonly Dictionary<long, ExchangeRateRecord> _rows = [];
        private long _nextId = 100;

        public int Locks { get; private set; }

        /// <summary>Inserts, updates and deletes.</summary>
        public int Writes { get; private set; }

        public void Add(long id, DateOnly rateDt, string currencyCode, decimal rate) => _rows[id] = new(id, rateDt, currencyCode, rate, Read);

        public Task LockAsync(IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Locks++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ExchangeRateRecord>> FindAsync(
            DateOnly first, DateOnly last, string? currencyCode, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExchangeRateRecord>>(_rows.Values
                .Where(r => r.RateDt >= first && r.RateDt <= last && (currencyCode is null || r.CurrencyCode == currencyCode))
                .OrderByDescending(r => r.RateDt).ThenBy(r => r.CurrencyCode, StringComparer.Ordinal)
                .ToList());

        public Task<IReadOnlyList<ExchangeRateRecord>> FindByIdsAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExchangeRateRecord>>(ids.Where(_rows.ContainsKey).Select(id => _rows[id]).ToList());

        public Task<IReadOnlyList<ExchangeRateRecord>> FindByKeysAsync(
            IReadOnlyCollection<(DateOnly RateDt, string CurrencyCode)> keys, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExchangeRateRecord>>(_rows.Values.Where(r => keys.Contains((r.RateDt, r.CurrencyCode))).ToList());

        public Task<decimal?> FindLatestAsync(string currencyCode, DateOnly onOrBefore, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(_rows.Values.Where(r => r.CurrencyCode == currencyCode && r.RateDt <= onOrBefore)
                .OrderByDescending(r => r.RateDt).Select(r => (decimal?)r.Rate).FirstOrDefault());

        public Task<long> InsertAsync(ExchangeRateFields fields, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Writes++;
            var id = _nextId++;
            _rows[id] = new(id, fields.RateDt, fields.CurrencyCode, fields.Rate, Read.AddDays(1));
            return Task.FromResult(id);
        }

        public Task<bool> UpdateAsync(
            long id, ExchangeRateFields fields, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Writes++;
            if (_rows[id].UpdatedAt != expectedUpdatedAt)
            {
                return Task.FromResult(false);
            }
            _rows[id] = new(id, fields.RateDt, fields.CurrencyCode, fields.Rate, expectedUpdatedAt.AddSeconds(1));
            return Task.FromResult(true);
        }

        public Task DeleteAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Writes += ids.Count;
            foreach (var id in ids)
            {
                _rows.Remove(id);
            }
            return Task.CompletedTask;
        }
    }
}
