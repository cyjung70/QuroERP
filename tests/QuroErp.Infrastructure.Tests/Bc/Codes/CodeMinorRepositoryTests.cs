using Dapper;
using FluentAssertions;
using QuroErp.Application.Bc.Codes;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Common.Codes;
using QuroErp.Application.Common.I18n;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Bc.Codes;
using QuroErp.Domain.Common;
using QuroErp.Domain.Common.Codes;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Bc.Codes;

/// <summary>Minor code management against PostgreSQL (BC-UC-008, seed 0500). Repository tests roll back by not committing.</summary>
[Collection(AuthSchemaDefinition.Name)]
public class CodeMinorRepositoryTests(AuthSchemaFixture db)
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static readonly CodeMinorFields Piece =
        new(new Dictionary<string, string> { ["en"] = "Piece", ["ko"] = "낱개" }, 900, true, "P", null);

    private CodeMinorService Service =>
        new(db.Get<ICodeMinorRepository>(), db.Get<ITranslationRepository>(), db.Get<ILocalizationRepository>(), db.Get<IUnitOfWorkFactory>());

    [DatabaseFact, Trait("Id", "BC-UC-008-IT-01")]
    public async Task InsertUpdateNamesAndDelete_RoundTrip()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<ICodeMinorRepository>();
        var unitId = (await repository.FindMajorIdAsync(CodeMajors.Unit, forUpdate: true, uow.Transaction, None))!.Value;

        var id = await repository.InsertAsync(unitId, "PC", Piece, "kim", uow.Transaction, None);
        await repository.ReplaceNamesAsync(id, Piece.Names, "kim", uow.Transaction, None);
        var inserted = (await repository.FindByMajorAsync(unitId, uow.Transaction, None)).Single(m => m.Id == id);
        var stale = await repository.UpdateAsync(id, Piece with { SortOrder = 901 }, inserted.UpdatedAt.AddSeconds(-1), "lee", uow.Transaction, None);
        var updated = await repository.UpdateAsync(id, Piece with { UseYn = false, Attr1 = null }, inserted.UpdatedAt, "lee", uow.Transaction, None);
        await repository.ReplaceNamesAsync(id, new Dictionary<string, string> { ["en"] = "Each piece" }, "lee", uow.Transaction, None);
        var names = await db.Get<ITranslationRepository>().FindAsync(TranslationTarget.CodeMinor, [id], uow.Transaction, None);
        var after = (await repository.FindByMajorAsync(unitId, uow.Transaction, None)).Single(m => m.Id == id);
        await repository.DeleteAsync([id], uow.Transaction, None);
        var translationsLeft = await uow.Transaction.Connection!.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM sys_code_minor_translation WHERE minor_id = @Id", new { Id = id }, uow.Transaction);

        inserted.Should().BeEquivalentTo(new { MinorCode = "PC", SortOrder = 900, UseYn = true, Attr1 = "P" });
        stale.Should().BeFalse();
        updated.Should().BeTrue();
        after.Should().BeEquivalentTo(new { SortOrder = 900, UseYn = false, Attr1 = (string?)null });
        names.Select(n => (n.LangCode, n.Name)).Should().Equal(("en", "Each piece"));
        translationsLeft.Should().Be(0);
        (await repository.FindByMajorAsync(unitId, uow.Transaction, None)).Should().NotContain(m => m.Id == id);
    }

    [DatabaseFact, Trait("Id", "SD-UT-910")]
    public async Task FindMajorId_FindsAMajorNotInUse_ButNotAnUnknownOne()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<ICodeMinorRepository>();
        await uow.Transaction.Connection!.ExecuteAsync("UPDATE sys_code_major SET use_yn = false WHERE major_code = 'TERMS'", transaction: uow.Transaction);

        (await repository.FindMajorIdAsync("TERMS", forUpdate: false, uow.Transaction, None)).Should().NotBeNull();
        (await repository.FindMajorIdAsync("NO_SUCH_MAJOR", forUpdate: false, uow.Transaction, None)).Should().BeNull();
    }

    [DatabaseFact, Trait("Id", "SD-UT-914")]
    public async Task FindUsed_ReturnsTheCodesThatABusinessColumnStores()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        await uow.Transaction.Connection!.ExecuteAsync(
            """
            INSERT INTO sys_menu (menu_code, name, type, module, valid_flag, created_by, updated_by)
            VALUES ('ZZCODE', 'Code test', 'M', 'bc', 'S', 'test', 'test')
            """,
            transaction: uow.Transaction);

        var used = await db.Get<ICodeMinorRepository>().FindUsedAsync(CodeMajors.ValidFlag, ["S", "X"], uow.Transaction, None);

        used.Should().BeEquivalentTo(["S"]);
    }

    [DatabaseFact, Trait("Id", "SD-UT-914")]
    public async Task EveryUsageColumn_ExistsInTheSchema()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<ICodeMinorRepository>();

        foreach (var major in CodeUsages.Columns.Keys)
        {
            var used = await repository.FindUsedAsync(major, ["NO_SUCH_CODE"], uow.Transaction, None);
            used.Should().BeEmpty(major);
        }
    }

    [DatabaseFact, Trait("Id", "SD-UT-915")]
    public async Task ACodeSetNotInUse_LeavesTheListsAndFailsTheSaveCheck()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var minors = db.Get<ICodeMinorRepository>();
        var codes = db.Get<ICodeRepository>();
        var termsId = (await minors.FindMajorIdAsync("TERMS", forUpdate: true, uow.Transaction, None))!.Value;
        var term = (await minors.FindByMajorAsync(termsId, uow.Transaction, None))[0];
        await minors.UpdateAsync(term.Id, new(new Dictionary<string, string> { ["en"] = "x" }, term.SortOrder, false, null, null), term.UpdatedAt,
            "kim", uow.Transaction, None);

        (await codes.FindActiveMinorsAsync("TERMS", uow.Transaction, None))!.Should().NotContain(m => m.MinorCode == term.MinorCode);
        (await codes.ExistsActiveAsync("TERMS", term.MinorCode, uow.Transaction, None)).Should().BeFalse();
    }

    [DatabaseFact, Trait("Id", "BC-UC-008-T02")]
    public async Task Save_WithAFailingRow_StoresNothing()
    {
        var before = await Service.SearchAsync("CART_NO", "en", None);

        var act = () => Service.SaveAsync(new SaveCodeMinorsRequest(
            "CART_NO",
            [new CodeMinorInsert("9", new Dictionary<string, string?> { ["en"] = "Cart 9" }, 90, null, null, null)],
            [new CodeMinorUpdate(before[0].Id, "X", new Dictionary<string, string?> { ["en"] = "Cart 1" }, 10, true, null, null, before[0].UpdatedAt)],
            null), "en", "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(CodeMinorRules.CodeChangedCode);
        (await Service.SearchAsync("CART_NO", "en", None)).Should().BeEquivalentTo(before);
    }
}
