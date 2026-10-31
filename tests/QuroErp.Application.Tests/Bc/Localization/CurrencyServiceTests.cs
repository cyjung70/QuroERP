using System.Data;
using FluentAssertions;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Common;
using QuroErp.Application.Common.I18n;
using QuroErp.Application.Tests.Common.Auth;
using QuroErp.Contracts.Bc.Localization;
using QuroErp.Contracts.Common;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Common;

namespace QuroErp.Application.Tests.Bc.Localization;

/// <summary>Currency management (BC-UC-026, BC-BR-404, BC-BR-411, BC-BR-428, BC-BR-312) with fake storage.</summary>
public class CurrencyServiceTests
{
    private static readonly DateTimeOffset Read = new(2026, 10, 30, 0, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly FakeCurrencyRepository _currencies = new();
    private readonly FakeLocalizationRepository _localization = new() { Company = new("KR", "KRW", "ko", "DOCUMENT", "Asia/Seoul") };
    private readonly FakeUnitOfWorkFactory _uow = new();
    private readonly CurrencyService _service;

    public CurrencyServiceTests()
    {
        _service = new CurrencyService(_currencies, _currencies, _localization, _uow);
        _currencies.Add(1, "KRW", 0, "KR", new() { ["en"] = "Korean Won", ["ko"] = "대한민국 원" });
        _currencies.Add(2, "USD", 2, "US", new() { ["en"] = "US Dollar" });
        _currencies.Add(3, "EUR", 2, null, new() { ["en"] = "Euro" }, useYn: false);
        _currencies.Used.Add("USD");
    }

    private static BatchRequest<CurrencySaveRow, CurrencyKey> Batch(
        CurrencySaveRow[]? added = null, CurrencySaveRow[]? updated = null, CurrencyKey[]? deleted = null) =>
        new(added ?? [], updated ?? [], deleted ?? []);

    private static Dictionary<string, string?> En(string name) => new() { ["en"] = name };

    [Fact, Trait("Id", "BC-UC-026")]
    public async Task Search_ReturnsEveryCurrencyInCodeOrder_WithNamesAndDocumentUse()
    {
        var all = await _service.SearchAsync("ko", null, None);
        var inUse = await _service.SearchAsync("en", true, None);

        all.Select(c => (c.CurrencyCode, c.Name, c.UseYn, c.InUseByDocuments)).Should().Equal(
            ("EUR", "Euro", false, false), ("KRW", "대한민국 원", true, false), ("USD", "US Dollar", true, true));
        inUse.Select(c => c.CurrencyCode).Should().Equal("KRW", "USD");
    }

    [Fact, Trait("Id", "BC-UC-026")]
    public async Task Save_AddsChangesAndDeletes_ThenCommitsOnce()
    {
        var after = await _service.SaveAsync(Batch(
            added: [new CurrencySaveRow(null, " jpy ", null, "jp", null, new Dictionary<string, string?> { ["en"] = "Yen", ["ko"] = "엔" }, null)],
            updated: [new CurrencySaveRow(2, "USD", 2, "US", true, new Dictionary<string, string?> { ["en"] = "U.S. Dollar" }, Read)],
            deleted: [new CurrencyKey(3, Read)]), "en", "kim", None);

        after.Select(c => (c.CurrencyCode, c.Name, c.Decimals, c.CountryCode)).Should().Equal(
            ("JPY", "Yen", 0, "JP"), ("KRW", "Korean Won", 0, "KR"), ("USD", "U.S. Dollar", 2, "US"));
        _uow.Commits.Should().Be(1);
    }

    [Fact, Trait("Id", "BC-UC-026-T01"), Trait("Rule", "BC-BR-411")]
    public async Task Save_AnInvalidCode_WritesAndCommitsNothing()
    {
        var act = () => _service.SaveAsync(Batch(
            added: [new CurrencySaveRow(null, "GBP", 2, null, null, En("Pound"), null), new CurrencySaveRow(null, "EU", 2, null, null, En("X"), null)]),
            "en", "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(CurrencyRules.InvalidCode);
        _currencies.Writes.Should().Be(0);
        _uow.Commits.Should().Be(0);
    }

    [Fact, Trait("Id", "BC-UT-411"), Trait("Rule", "BC-BR-411")]
    public async Task Save_AnExistingCode_IsBcE411_ButADeletedCodeCanBeAddedAgain()
    {
        var duplicate = () => _service.SaveAsync(Batch(added: [new CurrencySaveRow(null, "usd", 2, null, null, En("Dollar"), null)]), "en", "kim", None);

        (await duplicate.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal("USD");

        var after = await _service.SaveAsync(Batch(
            added: [new CurrencySaveRow(null, "EUR", 2, null, true, En("Euro"), null)], deleted: [new CurrencyKey(3, Read)]), "en", "kim", None);

        after.Single(c => c.CurrencyCode == "EUR").UseYn.Should().BeTrue();
    }

    [Theory, Trait("Id", "BC-UC-026-T02"), Trait("Id", "BC-UC-026-T03"), Trait("Rule", "BC-BR-411"), Trait("Rule", "BC-BR-428")]
    [InlineData(0, "US", "BC-E-411")]
    [InlineData(2, null, "BC-E-428")]
    public async Task Save_DecimalsOrCountryOfACurrencyDocumentsUse_Is409(int decimals, string? country, string error)
    {
        var act = () => _service.SaveAsync(Batch(updated: [new CurrencySaveRow(2, null, decimals, country, true, En("US Dollar"), Read)]), "en", "kim", None);

        (await act.Should().ThrowAsync<ConflictException>()).Which.Should().BeEquivalentTo(new { Code = error, Args = new object[] { "USD" } });
        _currencies.Writes.Should().Be(0);
    }

    [Fact, Trait("Id", "BC-UT-411"), Trait("Rule", "BC-BR-411"), Trait("Rule", "BC-BR-428")]
    public async Task Save_DecimalsAndCountryOfACurrencyNoDocumentUses_Change()
    {
        var after = await _service.SaveAsync(Batch(updated: [new CurrencySaveRow(1, "KRW", 2, null, true, En("Korean Won"), Read)]), "en", "kim", None);

        after.Single(c => c.CurrencyCode == "KRW").Should().BeEquivalentTo(new { Decimals = 2, CountryCode = (string?)null });
    }

    [Fact, Trait("Id", "BC-UT-411"), Trait("Rule", "BC-BR-411")]
    public async Task Save_ADifferentCodeForAStoredRow_IsBcE411()
    {
        var act = () => _service.SaveAsync(Batch(updated: [new CurrencySaveRow(1, "KRX", 0, "KR", true, En("Won"), Read)]), "en", "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal("KRW");
    }

    [Fact, Trait("Id", "BC-UC-026-T04"), Trait("Rule", "BC-BR-404")]
    public async Task Save_WithoutAnEnName_IsBcE404()
    {
        var act = () => _service.SaveAsync(Batch(added: [new CurrencySaveRow(null, "GBP", 2, null, null, new Dictionary<string, string?> { ["ko"] = "파운드" }, null)]), "en", "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Should().BeEquivalentTo(
            new { Code = CurrencyRules.NameRequiredCode, Args = new object[] { "GBP" } });
    }

    [Fact, Trait("Id", "BC-UT-405"), Trait("Rule", "BC-BR-405")]
    public async Task Save_ANameInALanguageNotInUse_IsBcE405()
    {
        _localization.Languages.RemoveAll(l => l.LangCode == "sw");

        var act = () => _service.SaveAsync(Batch(added: [new CurrencySaveRow(null, "GBP", 2, null, null, new Dictionary<string, string?> { ["en"] = "Pound", ["sw"] = "Pauni" }, null)]),
            "en", "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Should().BeEquivalentTo(new { Code = CurrencyService.LanguageNotInUseCode, Args = new object[] { "sw" } });
    }

    [Fact, Trait("Id", "BC-UT-312"), Trait("Rule", "BC-BR-312")]
    public async Task Save_TheBaseCurrencyNotInUse_IsBcE312()
    {
        var act = () => _service.SaveAsync(Batch(updated: [new CurrencySaveRow(1, "KRW", 0, "KR", false, En("Korean Won"), Read)]), "en", "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(CurrencyRules.BaseCurrencyCode);
    }

    [Fact, Trait("Id", "BC-UC-026")]
    public async Task Save_AStaleRow_Is409_AndWritesNothing()
    {
        var act = () => _service.SaveAsync(Batch(deleted: [new CurrencyKey(3, Read.AddSeconds(1))]), "en", "kim", None);

        await act.Should().ThrowAsync<ConcurrencyException>();
        _currencies.Writes.Should().Be(0);
    }

    private sealed class FakeCurrencyRepository : ICurrencyRepository, ITranslationRepository
    {
        private readonly Dictionary<long, CurrencyRecord> _rows = [];
        private readonly Dictionary<long, Dictionary<string, string>> _names = [];
        private long _nextId = 100;

        public HashSet<string> Used { get; } = [];

        public int Writes { get; private set; }

        public void Add(long id, string code, int decimals, string? country, Dictionary<string, string> names, bool useYn = true)
        {
            _rows[id] = new CurrencyRecord(id, code, decimals, country, useYn, Read);
            _names[id] = names;
        }

        public Task<IReadOnlyList<CurrencyRecord>> FindAllAsync(bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CurrencyRecord>>(_rows.Values.OrderBy(r => r.CurrencyCode, StringComparer.Ordinal).ToList());

        public Task<IReadOnlySet<string>> FindUsedByDocumentsAsync(
            IReadOnlyCollection<string> currencyCodes, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(currencyCodes.Where(Used.Contains).ToHashSet());

        public Task<long> InsertAsync(string currencyCode, CurrencyFields fields, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Writes++;
            var id = _nextId++;
            _rows[id] = new CurrencyRecord(id, currencyCode, fields.Decimals, fields.CountryCode, fields.UseYn, Read.AddDays(1));
            return Task.FromResult(id);
        }

        public Task<bool> UpdateAsync(
            long id, CurrencyFields fields, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Writes++;
            var row = _rows[id];
            _rows[id] = row with { Decimals = fields.Decimals, CountryCode = fields.CountryCode, UseYn = fields.UseYn, UpdatedAt = Read.AddDays(1) };
            return Task.FromResult(row.UpdatedAt == expectedUpdatedAt);
        }

        public Task SaveNamesAsync(
            long currencyId, IReadOnlyDictionary<string, string?> names, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Writes++;
            var stored = _names.TryGetValue(currencyId, out var current) ? current : _names[currencyId] = [];
            foreach (var (lang, name) in names)
            {
                if (name is null)
                {
                    stored.Remove(lang);
                }
                else
                {
                    stored[lang] = name;
                }
            }
            return Task.CompletedTask;
        }

        public Task DeleteAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Writes += ids.Count;
            foreach (var id in ids)
            {
                _rows.Remove(id);
                _names.Remove(id);
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TranslationRow>> FindAsync(
            TranslationTarget target, IReadOnlyCollection<long> ownerIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TranslationRow>>(ownerIds
                .SelectMany(id => _names.GetValueOrDefault(id, []).Select(n => new TranslationRow(id, n.Key, n.Value)))
                .ToList());
    }
}
