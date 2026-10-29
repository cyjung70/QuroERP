using Dapper;
using FluentAssertions;
using QuroErp.Application.Bc.Codes;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Common.I18n;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Bc.Codes;
using QuroErp.Domain.Common;
using QuroErp.Domain.Common.Codes;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Bc.Codes;

/// <summary>Major code management against PostgreSQL (BC-UC-023, seed 0500). Repository tests roll back by not committing.</summary>
[Collection(AuthSchemaDefinition.Name)]
public class CodeMajorRepositoryTests(AuthSchemaFixture db)
{
    private static readonly CancellationToken None = CancellationToken.None;

    private CodeMajorService Service =>
        new(db.Get<ICodeMajorRepository>(), db.Get<ITranslationRepository>(), db.Get<ILocalizationRepository>(), db.Get<IUnitOfWorkFactory>());

    [DatabaseFact, Trait("Id", "BC-UC-023-T07")]
    public async Task InsertUpdateNamesAndDelete_RoundTrip_NamesInTheTranslationTable_UnsentLanguagesKept()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<ICodeMajorRepository>();

        var id = await repository.InsertAsync("ZZ_TEST", true, "kim", uow.Transaction, None);
        await repository.SaveNamesAsync(id, new Dictionary<string, string?> { ["en"] = "Test", ["ko"] = "시험" }, "kim", uow.Transaction, None);
        var inserted = (await repository.FindAsync("ZZ_", forUpdate: true, uow.Transaction, None)).Single();
        var stale = await repository.UpdateAsync(id, false, inserted.UpdatedAt.AddSeconds(-1), "lee", uow.Transaction, None);
        var updated = await repository.UpdateAsync(id, false, inserted.UpdatedAt, "lee", uow.Transaction, None);
        await repository.SaveNamesAsync(id, new Dictionary<string, string?> { ["en"] = "Tested" }, "lee", uow.Transaction, None);
        var merged = await db.Get<ITranslationRepository>().FindAsync(TranslationTarget.CodeMajor, [id], uow.Transaction, None);
        await repository.SaveNamesAsync(id, new Dictionary<string, string?> { ["en"] = "Tested", ["ko"] = null }, "lee", uow.Transaction, None);
        var names = await db.Get<ITranslationRepository>().FindAsync(TranslationTarget.CodeMajor, [id], uow.Transaction, None);
        var after = (await repository.FindAsync("ZZ_TEST", forUpdate: false, uow.Transaction, None)).Single();
        await repository.DeleteAsync([id], uow.Transaction, None);
        var translationsLeft = await uow.Transaction.Connection!.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM sys_code_major_translation WHERE major_id = @Id", new { Id = id }, uow.Transaction);

        inserted.Should().BeEquivalentTo(new { Id = id, MajorCode = "ZZ_TEST", UseYn = true, MinorCount = 0 });
        stale.Should().BeFalse();
        updated.Should().BeTrue();
        after.UseYn.Should().BeFalse();
        merged.Select(n => (n.LangCode, n.Name)).Should().BeEquivalentTo([("en", "Tested"), ("ko", "시험")]);
        names.Select(n => (n.LangCode, n.Name)).Should().Equal(("en", "Tested"));
        translationsLeft.Should().Be(0);
        (await repository.FindAsync("ZZ_", forUpdate: false, uow.Transaction, None)).Should().BeEmpty();
    }

    [DatabaseFact, Trait("Id", "SD-UT-903")]
    public async Task Find_CountsEveryMinorCode_InUseOrNot_AndFiltersByCodePrefix()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var minors = await uow.Transaction.Connection!.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM sys_code_minor n JOIN sys_code_major m ON m.id = n.major_id WHERE m.major_code = 'UNIT'", transaction: uow.Transaction);

        var all = await db.Get<ICodeMajorRepository>().FindAsync(null, forUpdate: true, uow.Transaction, None);
        var unit = await db.Get<ICodeMajorRepository>().FindAsync("UNI", forUpdate: false, uow.Transaction, None);

        all.Select(m => m.MajorCode).Should().BeInAscendingOrder(StringComparer.Ordinal).And.Contain(CodeMajors.Unit);
        unit.Should().ContainSingle(m => m.MajorCode == CodeMajors.Unit).Which.MinorCount.Should().Be(minors).And.BePositive();
        unit.Should().OnlyContain(m => m.MajorCode.StartsWith("UNI", StringComparison.Ordinal));
    }

    [DatabaseFact, Trait("Id", "SD-UT-904")]
    public async Task EveryProtectedMajor_IsSeeded()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);

        var all = await db.Get<ICodeMajorRepository>().FindAsync(null, forUpdate: false, uow.Transaction, None);

        all.Select(m => m.MajorCode).Should().Contain(CodeMajorRules.ProtectedMajors);
    }

    [DatabaseFact, Trait("Id", "BC-UC-023-T05")]
    public async Task Save_WithOneFailingRowOfThree_StoresNothing()
    {
        var before = await Service.SearchAsync(null, "en", None);
        var terms = before.Single(m => m.MajorCode == "TERMS");
        var unit = before.Single(m => m.MajorCode == CodeMajors.Unit);

        var act = () => Service.SaveAsync(new SaveCodeMajorsRequest(
            [new CodeMajorInsert("ZZ_TEST", new Dictionary<string, string?> { ["en"] = "Test" }, null)],
            [new CodeMajorUpdate(terms.Id, null, new Dictionary<string, string?> { ["en"] = "Payment terms" }, true, terms.UpdatedAt)],
            [new CodeMajorDelete(unit.Id, unit.UpdatedAt)]), "en", "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(CodeMajorRules.ProtectedCode);
        (await Service.SearchAsync(null, "en", None)).Should().BeEquivalentTo(before);
    }
}
