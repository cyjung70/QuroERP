using Dapper;
using FluentAssertions;
using Npgsql;
using QuroERP.Database;
using QuroErp.Application.Common.Auth;
using QuroErp.Application.Common.Persistence;
using QuroErp.Infrastructure.Common.Persistence;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Common.Auth;

/// <summary>sys_app_user.lang_code (사용자 언어 설정) 통합 테스트 (TASK-CORE-004, 실제 PostgreSQL).</summary>
[Collection(AuthSchemaDefinition.Name)]
public class AppUserLangCodeTests(AuthSchemaFixture db)
{
    [DatabaseFact, Trait("Id", "CORE-004-IT-04")]
    public async Task FindByUserIdAsync_LangCodeSetOrNot_MapsLangCode()
    {
        await AdminAccount.CreateOrResetAsync(db.ConnectionString, "lang_en", "Pass-1", CancellationToken.None);
        await AdminAccount.CreateOrResetAsync(db.ConnectionString, "lang_none", "Pass-1", CancellationToken.None);
        await SetLangCodeAsync("lang_en", "en");

        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);
        var users = db.Get<IAppUserRepository>();
        var en = await users.FindByUserIdAsync("lang_en", uow.Transaction, CancellationToken.None);
        var none = await users.FindByUserIdAsync("lang_none", uow.Transaction, CancellationToken.None);

        en!.LangCode.Should().Be("en");
        none!.LangCode.Should().BeNull();
    }

    [DatabaseFact, Trait("Id", "CORE-004-IT-05")]
    public async Task Update_LangCodeNotInLanguageMaster_IsRejectedByForeignKey()
    {
        await AdminAccount.CreateOrResetAsync(db.ConnectionString, "lang_ja", "Pass-1", CancellationToken.None);

        var act = () => SetLangCodeAsync("lang_ja", "ja");

        (await act.Should().ThrowAsync<PostgresException>())
            .Which.ConstraintName.Should().Be("fk_sys_app_user_lang_code");
    }

    private async Task SetLangCodeAsync(string userId, string langCode)
    {
        await using var connection = await db.Get<IDbConnectionFactory>().OpenAsync(CancellationToken.None);
        await connection.ExecuteAsync(
            "UPDATE sys_app_user SET lang_code = @LangCode WHERE userid = @UserId", new { LangCode = langCode, UserId = userId });
    }
}
