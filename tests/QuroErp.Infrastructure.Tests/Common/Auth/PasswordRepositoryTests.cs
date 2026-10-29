using FluentAssertions;
using QuroERP.Database;
using QuroErp.Application.Common.Auth;
using QuroErp.Application.Common.Persistence;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Common.Auth;

/// <summary>Password change and history on real PostgreSQL (TASK-BC-003, BC-BR-055, BC-BR-058).</summary>
[Collection(AuthSchemaDefinition.Name)]
public class PasswordRepositoryTests(AuthSchemaFixture db)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 9, 0, 0, TimeSpan.Zero);

    [DatabaseFact, Trait("Id", "BC-003-IT-01")]
    public async Task ChangePasswordAsync_StoresHash_ClearsForcedChange_AndBumpsTokenVersion()
    {
        var id = await CreateUserAsync("pwd1");
        var users = db.Get<IAppUserRepository>();

        await using (var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None))
        {
            var before = (await users.LockByIdAsync(id, uow.Transaction, CancellationToken.None))!;
            before.TokenVersion.Should().Be(0);
            await users.ChangePasswordAsync(id, "new-hash", Now, "pwd1", uow.Transaction, CancellationToken.None);
            await users.ChangePasswordAsync(id, "newer-hash", Now.AddMinutes(1), "pwd1", uow.Transaction, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }

        await using var check = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);
        var saved = (await users.FindByIdAsync(id, check.Transaction, CancellationToken.None))!;
        saved.PasswordHash.Should().Be("newer-hash");
        saved.PasswordChangedAt.Should().Be(Now.AddMinutes(1));
        saved.MustChangePassword.Should().BeFalse();
        saved.PasswordValidDt.Should().BeNull();
        saved.TokenVersion.Should().Be(2);
    }

    [DatabaseFact, Trait("Id", "BC-003-IT-02")]
    public async Task PasswordHistory_FindRecent_ReturnsNewestFirstUpToCount()
    {
        var id = await CreateUserAsync("pwd2");
        var history = db.Get<IPasswordHistoryRepository>();

        await using (var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None))
        {
            foreach (var hash in new[] { "h1", "h2", "h3" })
            {
                await history.InsertAsync(id, hash, "pwd2", uow.Transaction, CancellationToken.None);
            }
            await uow.CommitAsync(CancellationToken.None);
        }

        await using var check = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);
        (await history.FindRecentAsync(id, 2, check.Transaction, CancellationToken.None)).Should().Equal("h3", "h2");
    }

    private async Task<long> CreateUserAsync(string userId)
    {
        await AdminAccount.CreateOrResetAsync(db.ConnectionString, userId, "Pass-1", CancellationToken.None);
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);
        return (await db.Get<IAppUserRepository>().FindByUserIdAsync(userId, uow.Transaction, CancellationToken.None))!.Id;
    }
}
