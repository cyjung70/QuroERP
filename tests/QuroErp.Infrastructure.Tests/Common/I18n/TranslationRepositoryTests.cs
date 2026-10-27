using Dapper;
using FluentAssertions;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Common.I18n;
using QuroErp.Application.Common.Persistence;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Common.I18n;

/// <summary>번역 테이블 조회 통합 테스트 (TASK-CORE-004, 실제 PostgreSQL, Seed 0501 번역 사용).</summary>
[Collection(AuthSchemaDefinition.Name)]
public class TranslationRepositoryTests(AuthSchemaFixture db)
{
    [DatabaseFact, Trait("Id", "CORE-004-IT-01")]
    public async Task FindAsync_SeededCodeMajor_ReturnsKoAndEnRows()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);
        var gradeId = await MajorIdAsync(uow, "GRADE");

        var rows = await db.Get<ITranslationRepository>().FindAsync(
            TranslationTarget.CodeMajor, [gradeId], uow.Transaction, CancellationToken.None);

        rows.Should().BeEquivalentTo(new[]
        {
            new TranslationRow(gradeId, "ko", "등급"),
            new TranslationRow(gradeId, "en", "Grade"),
        });
    }

    [DatabaseFact, Trait("Id", "CORE-004-IT-02")]
    public async Task GetNamesAsync_EnOnlyCode_FallsBackToEnForKo()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);
        var conn = uow.Transaction.Connection!;
        var majorId = await conn.ExecuteScalarAsync<long>(
            "INSERT INTO sys_code_major (major_code, created_by, updated_by) VALUES ('T_EN_ONLY', 't', 't') RETURNING id",
            transaction: uow.Transaction);
        var minorId = await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO sys_code_minor (major_id, minor_code, sort_order, created_by, updated_by)
            VALUES (@MajorId, 'A', 1, 't', 't') RETURNING id
            """,
            new { MajorId = majorId }, uow.Transaction);
        await conn.ExecuteAsync(
            "INSERT INTO sys_code_minor_translation (minor_id, lang_code, name, created_by, updated_by) VALUES (@MinorId, 'en', 'Only English', 't', 't')",
            new { MinorId = minorId }, uow.Transaction);

        var service = new TranslationService(db.Get<ITranslationRepository>(), db.Get<ILocalizationRepository>());
        var names = await service.GetNamesAsync(TranslationTarget.CodeMinor, [minorId], "ko", uow.Transaction, CancellationToken.None);

        names.Should().BeEquivalentTo(new Dictionary<long, string> { [minorId] = "Only English" });
    }

    [DatabaseFact, Trait("Id", "CORE-004-IT-03")]
    public async Task FindAsync_ItemNameUnknownId_ReturnsEmpty()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);

        var rows = await db.Get<ITranslationRepository>().FindAsync(
            TranslationTarget.ItemName, [-1], uow.Transaction, CancellationToken.None);

        rows.Should().BeEmpty();
    }

    private static Task<long> MajorIdAsync(IUnitOfWork uow, string majorCode) =>
        uow.Transaction.Connection!.ExecuteScalarAsync<long>(
            "SELECT id FROM sys_code_major WHERE major_code = @MajorCode", new { MajorCode = majorCode }, uow.Transaction);
}
