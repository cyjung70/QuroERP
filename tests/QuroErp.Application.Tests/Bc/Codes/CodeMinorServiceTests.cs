using System.Data;
using FluentAssertions;
using QuroErp.Application.Bc.Codes;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Common;
using QuroErp.Application.Common.I18n;
using QuroErp.Application.Tests.Bc.Localization;
using QuroErp.Application.Tests.Common.Auth;
using QuroErp.Contracts.Bc.Codes;
using QuroErp.Domain.Common;
using QuroErp.Domain.Common.Codes;

namespace QuroErp.Application.Tests.Bc.Codes;

/// <summary>Minor code management (BC-UC-008, SD-BR-905, SD-BR-910~916) with fake storage.</summary>
public class CodeMinorServiceTests
{
    private static readonly DateTimeOffset Read = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly FakeCodeMinorRepository _minors = new();
    private readonly FakeLocalizationRepository _localization = new() { Company = new("US", "USD", "en", "DOCUMENT", "America/New_York") };
    private readonly FakeUnitOfWorkFactory _uow = new();
    private readonly CodeMinorService _service;

    public CodeMinorServiceTests()
    {
        _service = new CodeMinorService(_minors, _minors, _localization, _uow);
        _minors.AddMajor(1, CodeMajors.Unit);
        _minors.AddMajor(2, "TERMS");
        _minors.Add(11, 1, "EA", 10, new() { ["en"] = "Each", ["ko"] = "개" });
        _minors.Add(12, 1, "BOX", 20, new() { ["en"] = "Box" });
        _minors.Add(13, 1, "DZ", 30, new() { ["en"] = "Dozen" }, useYn: false);
        _minors.Add(21, 2, "NET30", 10, new() { ["en"] = "Net 30" });
    }

    [Fact, Trait("Id", "BC-UC-008-T03")]
    public async Task Search_ReturnsTheMajorsCodesInSortOrder_InUseOrNot_WithNamesInTheLanguage()
    {
        var codes = await _service.SearchAsync(" unit ", "ko", None);

        codes.Select(c => (c.MinorCode, c.Name, c.UseYn)).Should().Equal(("EA", "개", true), ("BOX", "Box", true), ("DZ", "Dozen", false));
        codes[0].Names.Should().Equal(new Dictionary<string, string> { ["en"] = "Each", ["ko"] = "개" });
    }

    [Theory, Trait("Id", "SD-UT-910")]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task Search_WithoutAMajor_Is400(string? majorCode)
    {
        var act = () => _service.SearchAsync(majorCode, "en", None);

        (await act.Should().ThrowAsync<BadRequestException>()).Which.Code.Should().Be(CodeMinorService.RequiredCode);
    }

    [Fact, Trait("Id", "SD-UT-910")]
    public async Task Search_AnUnknownMajor_Is404()
    {
        var act = () => _service.SearchAsync("NOPE", "en", None);

        (await act.Should().ThrowAsync<NotFoundException>()).Which.Args.Should().Equal("NOPE");
    }

    [Fact, Trait("Id", "BC-UC-008-T01")]
    public async Task Save_AddsChangesAndDeletes_ThenCommitsOnce()
    {
        var codes = await _service.SaveAsync(new SaveCodeMinorsRequest(
            "UNIT",
            [new CodeMinorInsert("pc", new Dictionary<string, string?> { ["en"] = "Piece", ["ko"] = "개당" }, 5, null, null, null)],
            [new CodeMinorUpdate(11, "EA", new Dictionary<string, string?> { ["en"] = "Each" }, 15, true, "1", null, Read)],
            [new CodeMinorDelete(13, Read)]), "en", "kim", None);

        codes.Select(c => (c.MinorCode, c.SortOrder)).Should().Equal(("PC", 5), ("EA", 15), ("BOX", 20));
        codes.Single(c => c.MinorCode == "EA").Should().BeEquivalentTo(new { Attr1 = "1", Names = new Dictionary<string, string> { ["en"] = "Each", ["ko"] = "개" } });
        codes.Single(c => c.MinorCode == "PC").Names.Should().ContainKey("ko");
        _uow.Commits.Should().Be(1);
    }

    [Fact, Trait("Id", "BC-UC-008-T02")]
    public async Task Save_WithAFailingRow_WritesAndCommitsNothing()
    {
        var act = () => _service.SaveAsync(new SaveCodeMinorsRequest(
            "UNIT",
            [new CodeMinorInsert("PC", new Dictionary<string, string?> { ["en"] = "Piece" }, 5, null, null, null)],
            [new CodeMinorUpdate(12, null, new Dictionary<string, string?> { ["en"] = "Box" }, 10, true, null, null, Read)],
            [new CodeMinorDelete(13, Read)]), "en", "kim", None);

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be(CodeMinorRules.InvalidSortOrderCode);
        _minors.Writes.Should().Be(0);
        _uow.Commits.Should().Be(0);
    }

    [Fact, Trait("Id", "SD-UT-905")]
    public async Task Save_WithAStaleRow_Is409_AndWritesNothing()
    {
        var act = () => _service.SaveAsync(new SaveCodeMinorsRequest("UNIT", null, null, [new CodeMinorDelete(13, Read.AddSeconds(1))]), "en", "kim", None);

        await act.Should().ThrowAsync<ConcurrencyException>();
        _minors.Writes.Should().Be(0);
    }

    [Fact, Trait("Id", "SD-UT-905")]
    public async Task Save_ARowOfAnotherMajor_Is409()
    {
        var act = () => _service.SaveAsync(new SaveCodeMinorsRequest("UNIT", null, null, [new CodeMinorDelete(21, Read)]), "en", "kim", None);

        await act.Should().ThrowAsync<ConcurrencyException>();
    }

    [Fact, Trait("Id", "SD-UT-911")]
    public async Task Save_AnExistingCode_IsSdE911_ButADeletedCodeCanBeAddedAgain()
    {
        var duplicate = () => _service.SaveAsync(new SaveCodeMinorsRequest(
            "UNIT", [new CodeMinorInsert("box", new Dictionary<string, string?> { ["en"] = "Box" }, 40, null, null, null)], null, null), "en", "kim", None);

        (await duplicate.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal("BOX");

        var codes = await _service.SaveAsync(new SaveCodeMinorsRequest(
            "UNIT", [new CodeMinorInsert("DZ", new Dictionary<string, string?> { ["en"] = "Dozen" }, 40, null, null, null)], null,
            [new CodeMinorDelete(13, Read)]), "en", "kim", None);

        codes.Single(c => c.MinorCode == "DZ").UseYn.Should().BeTrue();
    }

    [Fact, Trait("Id", "SD-UT-911")]
    public async Task Save_ANameInALanguageNotInUse_IsBcE405()
    {
        var act = () => _service.SaveAsync(new SaveCodeMinorsRequest(
            "UNIT", [new CodeMinorInsert("PC", new Dictionary<string, string?> { ["en"] = "Piece", ["fr"] = "Pièce" }, 40, null, null, null)], null, null),
            "en", "kim", None);

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be(CodeMinorService.LanguageNotInUseCode);
        error.Args.Should().Equal("fr");
    }

    [Fact, Trait("Id", "BC-UT-405")]
    public async Task Save_ChangesOnlyTheSentLanguages_AndKeepsTheOthers()
    {
        _minors.Add(14, 1, "PK", 40, new() { ["en"] = "Pack", ["ko"] = "팩", ["ja"] = "パック" });

        var codes = await _service.SaveAsync(new SaveCodeMinorsRequest(
            "UNIT", null, [new CodeMinorUpdate(14, null, new Dictionary<string, string?> { ["en"] = "Package", ["ko"] = " " }, 40, true, null, null, Read)], null),
            "en", "kim", None);

        codes.Single(c => c.MinorCode == "PK").Names.Should().Equal(new Dictionary<string, string> { ["en"] = "Package", ["ja"] = "パック" });
    }

    [Fact, Trait("Id", "SD-UT-912")]
    public async Task Save_ChangingTheCode_IsSdE912()
    {
        var act = () => _service.SaveAsync(new SaveCodeMinorsRequest(
            "UNIT", null, [new CodeMinorUpdate(12, "CASE", new Dictionary<string, string?> { ["en"] = "Box" }, 20, true, null, null, Read)], null),
            "en", "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(CodeMinorRules.CodeChangedCode);
    }

    [Fact, Trait("Id", "SD-UT-913")]
    public async Task Save_SwappingSortOrders_IsAllowed()
    {
        var codes = await _service.SaveAsync(new SaveCodeMinorsRequest(
            "UNIT", null,
            [
                new CodeMinorUpdate(11, null, new Dictionary<string, string?> { ["en"] = "Each" }, 20, true, null, null, Read),
                new CodeMinorUpdate(12, null, new Dictionary<string, string?> { ["en"] = "Box" }, 10, true, null, null, Read),
            ], null), "en", "kim", None);

        codes.Select(c => c.MinorCode).Should().Equal("BOX", "EA", "DZ");
    }

    [Fact, Trait("Id", "SD-UT-914")]
    public async Task Save_DeletingACodeThatDataStores_IsSdE914()
    {
        _minors.Used.Add("BOX");

        var act = () => _service.SaveAsync(new SaveCodeMinorsRequest("UNIT", null, null, [new CodeMinorDelete(12, Read)]), "en", "kim", None);

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be(CodeMinorRules.InUseCode);
        error.Args.Should().Equal("BOX");
    }

    [Theory, Trait("Id", "SD-UT-916")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Save_DeletingOrDisablingAConstantCode_IsSdE916(bool delete)
    {
        var request = delete
            ? new SaveCodeMinorsRequest("UNIT", null, null, [new CodeMinorDelete(11, Read)])
            : new SaveCodeMinorsRequest(
                "UNIT", null, [new CodeMinorUpdate(11, null, new Dictionary<string, string?> { ["en"] = "Each" }, 10, false, null, null, Read)], null);

        var act = () => _service.SaveAsync(request, "en", "kim", None);

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be(CodeMinorRules.ProtectedCode);
        error.Args.Should().Equal("EA");
    }

    /// <summary>Minor codes and their names in memory. Serves as the translation repository too.</summary>
    private sealed class FakeCodeMinorRepository : ICodeMinorRepository, ITranslationRepository
    {
        private readonly Dictionary<string, long> _majors = new(StringComparer.Ordinal);
        private readonly Dictionary<long, (long MajorId, CodeMinorRecord Row)> _rows = [];
        private readonly Dictionary<long, Dictionary<string, string>> _names = [];
        private long _nextId = 100;

        public HashSet<string> Used { get; } = [];

        public int Writes { get; private set; }

        public void AddMajor(long id, string code) => _majors[code] = id;

        public void Add(long id, long majorId, string code, int sortOrder, Dictionary<string, string> names, bool useYn = true)
        {
            _rows[id] = (majorId, new CodeMinorRecord(id, code, sortOrder, useYn, null, null, Read));
            _names[id] = names;
        }

        public Task<long?> FindMajorIdAsync(string majorCode, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(_majors.TryGetValue(majorCode, out var id) ? id : (long?)null);

        public Task<IReadOnlyList<CodeMinorRecord>> FindByMajorAsync(long majorId, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CodeMinorRecord>>(
                _rows.Values.Where(r => r.MajorId == majorId).Select(r => r.Row).OrderBy(r => r.SortOrder).ThenBy(r => r.MinorCode).ToList());

        public Task<IReadOnlySet<string>> FindUsedAsync(
            string majorCode, IReadOnlyCollection<string> minorCodes, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(minorCodes.Where(Used.Contains).ToHashSet());

        public Task<long> InsertAsync(
            long majorId, string minorCode, CodeMinorFields fields, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Writes++;
            var id = _nextId++;
            _rows[id] = (majorId, new CodeMinorRecord(id, minorCode, fields.SortOrder, fields.UseYn, fields.Attr1, fields.Attr2, Read.AddDays(1)));
            return Task.FromResult(id);
        }

        public Task<bool> UpdateAsync(
            long id, CodeMinorFields fields, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction,
            CancellationToken cancellationToken)
        {
            Writes++;
            var (majorId, row) = _rows[id];
            _rows[id] = (majorId, row with
            {
                SortOrder = fields.SortOrder,
                UseYn = fields.UseYn,
                Attr1 = fields.Attr1,
                Attr2 = fields.Attr2,
                UpdatedAt = Read.AddDays(1),
            });
            return Task.FromResult(row.UpdatedAt == expectedUpdatedAt);
        }

        public Task SaveNamesAsync(
            long minorId, IReadOnlyDictionary<string, string?> names, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Writes++;
            var stored = _names.TryGetValue(minorId, out var current) ? current : _names[minorId] = [];
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
