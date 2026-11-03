using System.Data;
using FluentAssertions;
using QuroErp.Application.Bc.Tax;
using QuroErp.Application.Common;
using QuroErp.Application.Common.I18n;
using QuroErp.Application.Tests.Bc.Localization;
using QuroErp.Application.Tests.Common.Auth;
using QuroErp.Contracts.Bc.Tax;
using QuroErp.Contracts.Common;
using QuroErp.Domain.Bc.Tax;
using QuroErp.Domain.Common;

namespace QuroErp.Application.Tests.Bc.Tax;

/// <summary>Tax code management (BC-UC-028, BC-BR-404, BC-BR-501~506) for a KR company with fake storage.</summary>
public class TaxCodeServiceTests
{
    private static readonly DateTimeOffset Read = new(2026, 10, 30, 0, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly FakeTaxCodeRepository _taxCodes = new();
    private readonly FakeLocalizationRepository _localization = new() { Company = new("KR", "KRW", "ko", "DOCUMENT", "Asia/Seoul") };
    private readonly FakeUnitOfWorkFactory _uow = new();
    private readonly TaxCodeService _service;

    public TaxCodeServiceTests()
    {
        _service = new TaxCodeService(_taxCodes, _taxCodes, _localization, _uow);
        _taxCodes.Add(11, "NONE", TaxKind.None, 0, new() { ["en"] = "No tax", ["ko"] = "세금 없음" });
        _taxCodes.Add(12, "VAT10", TaxKind.VatStandard, 10, new() { ["en"] = "VAT 10%", ["ko"] = "과세 10%" });
        _taxCodes.Add(13, "VAT0", TaxKind.VatZero, 0, new() { ["en"] = "Zero rate" });
        _taxCodes.UsedByDocuments.Add(12);
    }

    private static BatchRequest<TaxCodeSaveRow, TaxCodeKey> Batch(
        TaxCodeSaveRow[]? added = null, TaxCodeSaveRow[]? updated = null, TaxCodeKey[]? deleted = null) =>
        new(added ?? [], updated ?? [], deleted ?? []);

    private static Dictionary<string, string?> En(string name) => new() { ["en"] = name };

    private static TaxCodeSaveRow Row(long? id, string code, string kind, decimal? rate = null, bool? useYn = null, string name = "X") =>
        new(id, code, kind, rate, null, null, useYn, En(name), id is null ? null : Read);

    [Fact, Trait("Id", "BC-UC-028")]
    public async Task Search_ReturnsEveryCodeInCodeOrder_WithNamesAndDocumentUse_AndFilters()
    {
        var all = await _service.SearchAsync("ko", null, null, None);
        var zero = await _service.SearchAsync("en", true, "vat_zero", None);

        all.Select(t => (t.TaxCode, t.Name, t.Rate, t.InUseByDocuments)).Should().Equal(
            ("NONE", "세금 없음", 0m, false), ("VAT0", "Zero rate", 0m, false), ("VAT10", "과세 10%", 10m, true));
        zero.Select(t => t.TaxCode).Should().Equal("VAT0");
    }

    [Fact, Trait("Id", "BC-UC-028")]
    public async Task Save_AddsChangesAndDeletes_ThenCommitsOnce()
    {
        var after = await _service.SaveAsync(Batch(
            added: [new TaxCodeSaveRow(null, " vatex ", "VAT_EXEMPT", null, null, null, null, new Dictionary<string, string?> { ["en"] = "Exempt", ["ko"] = "면세" }, null)],
            updated: [new TaxCodeSaveRow(12, "VAT10", TaxKind.VatStandard, 10, null, true, false, En("VAT 10% incl."), Read)],
            deleted: [new TaxCodeKey(13, Read)]), "en", "kim", None);

        after.Select(t => (t.TaxCode, t.Name, t.TaxKind, t.PriceIncluded, t.UseYn)).Should().Equal(
            ("NONE", "No tax", "NONE", false, true), ("VAT10", "VAT 10% incl.", "VAT_STANDARD", true, false), ("VATEX", "Exempt", "VAT_EXEMPT", false, true));
        _uow.Commits.Should().Be(1);
    }

    [Fact, Trait("Id", "BC-UC-028-T01"), Trait("Id", "BC-UT-502"), Trait("Rule", "BC-BR-502")]
    public async Task Save_AKindNotOfTheCompanyCountry_IsBcE502_AndSavesNothing()
    {
        var act = () => _service.SaveAsync(Batch(added: [Row(null, "VAT5", TaxKind.VatStandard, 5), Row(null, "ST", TaxKind.SalesTax)]), "en", "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Should().BeEquivalentTo(new { Code = "BC-E-502", Args = new object[] { "ST" } });
        _taxCodes.Writes.Should().Be(0);
        _uow.Commits.Should().Be(0);
    }

    [Fact, Trait("Id", "BC-UT-501"), Trait("Rule", "BC-BR-501")]
    public async Task Save_ACodeThatStaysStored_OrIsSentTwice_IsBcE501()
    {
        var stored = () => _service.SaveAsync(Batch(added: [Row(null, "VAT0", TaxKind.VatZero)]), "en", "kim", None);
        var twice = () => _service.SaveAsync(Batch(added: [Row(null, "VATEX", TaxKind.VatExempt), Row(null, "vatex", TaxKind.VatExempt)]), "en", "kim", None);

        (await stored.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("BC-E-501");
        (await twice.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("BC-E-501");
    }

    [Fact, Trait("Id", "BC-UT-501"), Trait("Rule", "BC-BR-501")]
    public async Task Save_RenamingAndReusingADeletedCode_IsSaved()
    {
        var after = await _service.SaveAsync(Batch(
            added: [Row(null, "VAT0", TaxKind.VatZero, name: "New zero")],
            updated: [Row(12, "VAT10A", TaxKind.VatStandard, 10)],
            deleted: [new TaxCodeKey(13, Read)]), "en", "kim", None);

        after.Select(t => t.TaxCode).Should().Equal("NONE", "VAT0", "VAT10A");
    }

    [Theory, Trait("Id", "BC-UC-028-T04"), Trait("Id", "BC-UT-505"), Trait("Rule", "BC-BR-505")]
    [InlineData("delete")]
    [InlineData("disable")]
    [InlineData("rename")]
    [InlineData("kind")]
    public async Task Save_DeletingDisablingRenamingOrRekindingNone_Is409BcE505(string change)
    {
        var batch = change switch
        {
            "delete" => Batch(deleted: [new TaxCodeKey(11, Read)]),
            "disable" => Batch(updated: [Row(11, "NONE", TaxKind.None, useYn: false)]),
            "rename" => Batch(updated: [Row(11, "NOTAX", TaxKind.None)]),
            _ => Batch(updated: [Row(11, "NONE", TaxKind.VatExempt)]),
        };

        var act = () => _service.SaveAsync(batch, "en", "kim", None);

        (await act.Should().ThrowAsync<ConflictException>()).Which.Should().BeEquivalentTo(new { Code = "BC-E-505", Args = new object[] { "NONE" } });
        _taxCodes.Writes.Should().Be(0);
    }

    [Theory, Trait("Id", "BC-UC-028-T03"), Trait("Id", "BC-UT-506"), Trait("Rule", "BC-BR-506")]
    [InlineData("rate")]
    [InlineData("kind")]
    [InlineData("delete")]
    public async Task Save_ChangingTheRateOrKindOrDeletingACodeDocumentsUse_Is409BcE506(string change)
    {
        var batch = change switch
        {
            "rate" => Batch(updated: [Row(12, "VAT10", TaxKind.VatStandard, 11)]),
            "kind" => Batch(updated: [Row(12, "VAT10", TaxKind.VatZero)]),
            _ => Batch(deleted: [new TaxCodeKey(12, Read)]),
        };

        var act = () => _service.SaveAsync(batch, "en", "kim", None);

        (await act.Should().ThrowAsync<ConflictException>()).Which.Should().BeEquivalentTo(new { Code = "BC-E-506", Args = new object[] { "VAT10" } });
        _taxCodes.Writes.Should().Be(0);
    }

    [Fact, Trait("Id", "BC-UT-506"), Trait("Rule", "BC-BR-506")]
    public async Task Save_ACodeDocumentsUse_MayBeSetNotInUseAndRenamed()
    {
        var after = await _service.SaveAsync(Batch(updated: [Row(12, "VAT10OLD", TaxKind.VatStandard, 10, useYn: false)]), "en", "kim", None);

        after.Should().ContainSingle(t => t.Id == 12).Which.Should().BeEquivalentTo(new { TaxCode = "VAT10OLD", UseYn = false });
    }

    [Fact, Trait("Id", "BC-UT-506"), Trait("Rule", "BC-BR-506")]
    public async Task Save_ACodeAPartnerUses_CannotBeDeleted_ButItsRateMayChange()
    {
        _taxCodes.UsedByPartners.Add(13);

        var delete = () => _service.SaveAsync(Batch(deleted: [new TaxCodeKey(13, Read)]), "en", "kim", None);
        (await delete.Should().ThrowAsync<ConflictException>()).Which.Should().BeEquivalentTo(new { Code = "BC-E-506", Args = new object[] { "VAT0" } });

        var after = await _service.SaveAsync(Batch(updated: [Row(13, "VAT0", TaxKind.VatExempt)]), "en", "kim", None);
        after.Should().ContainSingle(t => t.Id == 13).Which.TaxKind.Should().Be(TaxKind.VatExempt);
    }

    [Fact, Trait("Id", "BC-UC-028")]
    public async Task Save_ARowChangedAfterItWasRead_IsAConflict()
    {
        var act = () => _service.SaveAsync(Batch(updated: [Row(13, "VAT0", TaxKind.VatZero) with { UpdatedAt = Read.AddSeconds(-1) }]), "en", "kim", None);

        await act.Should().ThrowAsync<ConcurrencyException>();
        _taxCodes.Writes.Should().Be(0);
    }

    [Fact, Trait("Id", "BC-UT-404"), Trait("Rule", "BC-BR-404")]
    public async Task Save_ANameInALanguageNotInUse_IsBcE405()
    {
        _localization.Languages.RemoveAll(l => l.LangCode == "sw");

        var act = () => _service.SaveAsync(Batch(added: [new TaxCodeSaveRow(null, "VATEX", TaxKind.VatExempt, null, null, null, null,
            new Dictionary<string, string?> { ["en"] = "Exempt", ["sw"] = "Msamaha" }, null)]), "en", "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Should().BeEquivalentTo(new { Code = "BC-E-405", Args = new object[] { "sw" } });
    }

    private sealed class FakeTaxCodeRepository : ITaxCodeRepository, ITranslationRepository
    {
        private readonly Dictionary<long, TaxCodeRecord> _rows = [];
        private readonly Dictionary<long, Dictionary<string, string>> _names = [];
        private long _nextId = 100;

        public HashSet<long> UsedByDocuments { get; } = [];

        public HashSet<long> UsedByPartners { get; } = [];

        /// <summary>Inserts, updates, name saves and deletes.</summary>
        public int Writes { get; private set; }

        public void Add(long id, string code, string kind, decimal rate, Dictionary<string, string> names)
        {
            _rows[id] = new TaxCodeRecord(id, code, kind, rate, null, null, false, true, Read);
            _names[id] = names;
        }

        public Task<IReadOnlyList<TaxCodeRecord>> FindAllAsync(bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TaxCodeRecord>>(_rows.Values.OrderBy(r => r.TaxCode, StringComparer.Ordinal).ToList());

        public Task<IReadOnlySet<long>> FindUsedByDocumentsAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<long>>(ids.Where(UsedByDocuments.Contains).ToHashSet());

        public Task<IReadOnlySet<long>> FindUsedByPartnersAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<long>>(ids.Where(UsedByPartners.Contains).ToHashSet());

        public Task<IReadOnlySet<long>> FindTaxAreasAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<long>>(new HashSet<long>());

        public Task<long> InsertAsync(string taxCode, TaxCodeFields fields, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Writes++;
            var id = _nextId++;
            _rows[id] = new TaxCodeRecord(id, taxCode, fields.TaxKind, fields.Rate, fields.TaxAreaId, null, fields.PriceIncluded, fields.UseYn, Read.AddDays(1));
            return Task.FromResult(id);
        }

        public Task<bool> UpdateAsync(
            long id, string taxCode, TaxCodeFields fields, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction,
            CancellationToken cancellationToken)
        {
            Writes++;
            if (_rows[id].UpdatedAt != expectedUpdatedAt)
            {
                return Task.FromResult(false);
            }
            _rows[id] = new TaxCodeRecord(id, taxCode, fields.TaxKind, fields.Rate, fields.TaxAreaId, null, fields.PriceIncluded, fields.UseYn, Read.AddDays(1));
            return Task.FromResult(true);
        }

        public Task SaveNamesAsync(
            long taxCodeId, IReadOnlyDictionary<string, string?> names, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Writes++;
            var stored = _names.TryGetValue(taxCodeId, out var current) ? current : _names[taxCodeId] = [];
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
            Task.FromResult<IReadOnlyList<TranslationRow>>(target != TranslationTarget.TaxCodeName
                ? []
                : ownerIds.SelectMany(id => _names.GetValueOrDefault(id, []).Select(n => new TranslationRow(id, n.Key, n.Value))).ToList());
    }
}
