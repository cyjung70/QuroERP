using System.Data;
using FluentAssertions;
using QuroErp.Application.Bc.Codes;
using QuroErp.Application.Common;
using QuroErp.Application.Common.I18n;
using QuroErp.Application.Tests.Bc.Localization;
using QuroErp.Application.Tests.Common.Auth;
using QuroErp.Contracts.Bc.Codes;
using QuroErp.Domain.Common;
using QuroErp.Domain.Common.Codes;

namespace QuroErp.Application.Tests.Bc.Codes;

/// <summary>Major code management (BC-UC-023, SD-BR-901~905) with fake storage.</summary>
public class CodeMajorServiceTests
{
    private static readonly DateTimeOffset Read = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly FakeCodeMajorRepository _majors = new();
    private readonly FakeLocalizationRepository _localization = new() { Company = new("US", "USD", "en", "DOCUMENT") };
    private readonly FakeUnitOfWorkFactory _uow = new();
    private readonly CodeMajorService _service;

    public CodeMajorServiceTests()
    {
        _service = new CodeMajorService(_majors, _majors, _localization, _uow);
        _majors.Add(1, CodeMajors.Unit, 3, new() { ["en"] = "Unit", ["ko"] = "단위" });
        _majors.Add(2, "TERMS", 2, new() { ["en"] = "Terms" });
        _majors.Add(3, "TEMP", 0, new() { ["en"] = "Temporary" });
    }

    [Fact, Trait("Id", "BC-UC-023-T07")]
    public async Task Search_ReturnsMajorsByCodePrefix_WithNamesInTheLanguageAndMinorCount()
    {
        var all = await _service.SearchAsync(null, "ko", None);
        var te = await _service.SearchAsync(" te ", "ko", None);

        all.Select(m => (m.MajorCode, m.Name, m.MinorCount)).Should().Equal(("TEMP", "Temporary", 0), ("TERMS", "Terms", 2), ("UNIT", "단위", 3));
        all[2].Names.Should().Equal(new Dictionary<string, string> { ["en"] = "Unit", ["ko"] = "단위" });
        te.Select(m => m.MajorCode).Should().Equal("TEMP", "TERMS");
    }

    [Fact, Trait("Id", "BC-UC-023-T07")]
    public async Task Save_AddsChangesAndDeletes_StoresNamesAsTranslations_ThenCommitsOnce()
    {
        var majors = await _service.SaveAsync(new SaveCodeMajorsRequest(
            [new CodeMajorInsert("ship_via", new Dictionary<string, string?> { ["en"] = "Ship Via", ["ko"] = "운송 방법" }, null)],
            [new CodeMajorUpdate(2, "terms", new Dictionary<string, string?> { ["en"] = "Payment Terms" }, false, Read)],
            [new CodeMajorDelete(3, Read)]), "en", "kim", None);

        majors.Select(m => (m.MajorCode, m.Name, m.UseYn)).Should().Equal(("SHIP_VIA", "Ship Via", true), ("TERMS", "Payment Terms", false), ("UNIT", "Unit", true));
        _majors.NamesOf("SHIP_VIA").Should().Equal(new Dictionary<string, string> { ["en"] = "Ship Via", ["ko"] = "운송 방법" });
        _uow.Commits.Should().Be(1);
    }

    [Fact, Trait("Id", "BC-UC-023-T05")]
    public async Task Save_WithOneFailingRowOfThree_WritesAndCommitsNothing()
    {
        var act = () => _service.SaveAsync(new SaveCodeMajorsRequest(
            [new CodeMajorInsert("SHIP_VIA", new Dictionary<string, string?> { ["en"] = "Ship Via" }, null)],
            [new CodeMajorUpdate(2, null, new Dictionary<string, string?> { ["en"] = "Payment Terms" }, true, Read)],
            [new CodeMajorDelete(1, Read)]), "en", "kim", None);

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be(CodeMajorRules.ProtectedCode);
        error.Args.Should().Equal(CodeMajors.Unit);
        _majors.Writes.Should().Be(0);
        _uow.Commits.Should().Be(0);
    }

    [Fact, Trait("Id", "SD-UT-905")]
    public async Task Save_WithAStaleOrMissingRow_Is409_AndWritesNothing()
    {
        var stale = () => _service.SaveAsync(new SaveCodeMajorsRequest(null, null, [new CodeMajorDelete(3, Read.AddSeconds(1))]), "en", "kim", None);
        var missing = () => _service.SaveAsync(new SaveCodeMajorsRequest(null, null, [new CodeMajorDelete(99, Read)]), "en", "kim", None);

        await stale.Should().ThrowAsync<ConcurrencyException>();
        await missing.Should().ThrowAsync<ConcurrencyException>();
        _majors.Writes.Should().Be(0);
    }

    [Theory, Trait("Id", "BC-UC-023-T01")]
    [InlineData("terms", "TERMS")]
    [InlineData("SO-STATUS", "SO-STATUS")]
    public async Task Save_AnExistingOrBadCode_IsSdE901(string code, string arg)
    {
        var act = () => _service.SaveAsync(new SaveCodeMajorsRequest(
            [new CodeMajorInsert(code, new Dictionary<string, string?> { ["en"] = "X" }, null)], null, null), "en", "kim", None);

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be(CodeMajorRules.InvalidCodeCode);
        error.Args.Should().Equal(arg);
    }

    [Fact, Trait("Id", "SD-UT-901")]
    public async Task Save_ADeletedCodeCanBeAddedAgain()
    {
        var majors = await _service.SaveAsync(new SaveCodeMajorsRequest(
            [new CodeMajorInsert("TEMP", new Dictionary<string, string?> { ["en"] = "Temp" }, null)], null, [new CodeMajorDelete(3, Read)]),
            "en", "kim", None);

        majors.Single(m => m.MajorCode == "TEMP").Name.Should().Be("Temp");
    }

    [Fact, Trait("Id", "SD-UT-901")]
    public async Task Save_ANameInALanguageNotInUse_IsBcE405()
    {
        var act = () => _service.SaveAsync(new SaveCodeMajorsRequest(
            [new CodeMajorInsert("SHIP_VIA", new Dictionary<string, string?> { ["en"] = "Ship Via", ["fr"] = "Expédition" }, null)], null, null),
            "en", "kim", None);

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be(CodeMinorService.LanguageNotInUseCode);
        error.Args.Should().Equal("fr");
    }

    [Fact, Trait("Id", "BC-UC-023-T02")]
    public async Task Save_ChangingTheCode_IsSdE902()
    {
        var act = () => _service.SaveAsync(new SaveCodeMajorsRequest(
            null, [new CodeMajorUpdate(2, "PAY_TERMS", new Dictionary<string, string?> { ["en"] = "Terms" }, true, Read)], null), "en", "kim", None);

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be(CodeMajorRules.CodeChangedCode);
        error.Args.Should().Equal("TERMS");
    }

    [Fact, Trait("Id", "BC-UC-023-T03")]
    public async Task Save_DeletingAMajorWithMinors_IsSdE903()
    {
        var act = () => _service.SaveAsync(new SaveCodeMajorsRequest(null, null, [new CodeMajorDelete(2, Read)]), "en", "kim", None);

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be(CodeMajorRules.HasMinorsCode);
        error.Args.Should().Equal("TERMS");
    }

    [Theory, Trait("Id", "BC-UC-023-T04")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Save_DeletingOrDisablingAConstantMajor_IsSdE904(bool delete)
    {
        var request = delete
            ? new SaveCodeMajorsRequest(null, null, [new CodeMajorDelete(1, Read)])
            : new SaveCodeMajorsRequest(null, [new CodeMajorUpdate(1, null, new Dictionary<string, string?> { ["en"] = "Unit" }, false, Read)], null);

        var act = () => _service.SaveAsync(request, "en", "kim", None);

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be(CodeMajorRules.ProtectedCode);
        error.Args.Should().Equal(CodeMajors.Unit);
    }

    /// <summary>Major codes and their names in memory. Serves as the translation repository too.</summary>
    private sealed class FakeCodeMajorRepository : ICodeMajorRepository, ITranslationRepository
    {
        private readonly Dictionary<long, CodeMajorRecord> _rows = [];
        private readonly Dictionary<long, Dictionary<string, string>> _names = [];
        private long _nextId = 100;

        public int Writes { get; private set; }

        public void Add(long id, string code, int minorCount, Dictionary<string, string> names)
        {
            _rows[id] = new CodeMajorRecord(id, code, true, minorCount, Read);
            _names[id] = names;
        }

        public Dictionary<string, string> NamesOf(string code) => _names[_rows.Values.Single(r => r.MajorCode == code).Id];

        public Task<IReadOnlyList<CodeMajorRecord>> FindAsync(
            string? codePrefix, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CodeMajorRecord>>(_rows.Values
                .Where(r => codePrefix is null || r.MajorCode.StartsWith(codePrefix, StringComparison.Ordinal))
                .OrderBy(r => r.MajorCode, StringComparer.Ordinal)
                .ToList());

        public Task<long> InsertAsync(string majorCode, bool useYn, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Writes++;
            var id = _nextId++;
            _rows[id] = new CodeMajorRecord(id, majorCode, useYn, 0, Read.AddDays(1));
            return Task.FromResult(id);
        }

        public Task<bool> UpdateAsync(
            long id, bool useYn, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Writes++;
            var row = _rows[id];
            _rows[id] = row with { UseYn = useYn, UpdatedAt = Read.AddDays(1) };
            return Task.FromResult(row.UpdatedAt == expectedUpdatedAt);
        }

        public Task ReplaceNamesAsync(
            long majorId, IReadOnlyDictionary<string, string> names, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Writes++;
            _names[majorId] = names.ToDictionary(n => n.Key, n => n.Value);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            if (ids.Count > 0)
            {
                Writes++;
            }
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
