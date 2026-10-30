using Dapper;
using FluentAssertions;
using QuroERP.Database;
using QuroErp.Application.Bc.UserProfiles;
using QuroErp.Application.Common.Auth;
using QuroErp.Application.Common.Persistence;
using QuroErp.Domain.Bc.UserProfiles;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Bc.UserProfiles;

/// <summary>My info reads and writes (BC-UC-003) on real PostgreSQL. Changes after the user is created roll back.</summary>
[Collection(AuthSchemaDefinition.Name)]
public class UserProfileRepositoryTests(AuthSchemaFixture db)
{
    private static readonly CancellationToken None = CancellationToken.None;

    [DatabaseFact, Trait("Id", "BC-UC-003-IT-01")]
    public async Task Find_WithoutOwnWarehouse_ShowsTheCompanyDefaultWarehouse()
    {
        var id = await CreateUserAsync("prof1");
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);

        var row = await db.Get<IUserProfileRepository>().FindAsync(id, forUpdate: true, uow.Transaction, None);

        row.Should().BeEquivalentTo(new { Id = id, UserId = "prof1", DefaultWarehouseId = (long?)null, DefaultWarehouseCode = "MAIN" });
    }

    [DatabaseFact, Trait("Id", "BC-UC-003-IT-02")]
    public async Task Update_ChecksUpdatedAt_AndSavesOnlyMyInfoFields()
    {
        var id = await CreateUserAsync("prof2");
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<IUserProfileRepository>();
        var warehouseId = await uow.Transaction.Connection!.ExecuteScalarAsync<long>(
            "INSERT INTO bc_warehouse (warehouse_code, warehouse_name, created_by, updated_by) VALUES ('PROF2', 'Second', 't', 't') RETURNING id",
            transaction: uow.Transaction);
        var before = (await repository.FindAsync(id, forUpdate: true, uow.Transaction, None))!;
        var roleBefore = (await db.Get<IAppUserRepository>().FindByIdAsync(id, uow.Transaction, None))!.RoleCode;
        var fields = new UserProfileFields("Prof Two", "p2@example.com", "010-2", "en", warehouseId);

        var stale = await repository.UpdateAsync(id, fields, before.UpdatedAt.AddSeconds(-1), "prof2", uow.Transaction, None);
        var saved = await repository.UpdateAsync(id, fields, before.UpdatedAt, "prof2", uow.Transaction, None);
        var after = (await repository.FindAsync(id, forUpdate: false, uow.Transaction, None))!;
        var user = (await db.Get<IAppUserRepository>().FindByIdAsync(id, uow.Transaction, None))!;

        stale.Should().BeFalse();
        saved.Should().BeTrue();
        after.Should().BeEquivalentTo(new
        {
            UserName = "Prof Two",
            Email = "p2@example.com",
            Phone = "010-2",
            LangCode = "en",
            DefaultWarehouseId = warehouseId,
            DefaultWarehouseCode = "PROF2",
        });
        after.UpdatedAt.Should().BeAfter(before.UpdatedAt);
        user.UserId.Should().Be("prof2");
        user.RoleCode.Should().Be(roleBefore);
    }

    [DatabaseFact, Trait("Id", "BC-UT-403")]
    public async Task InUseChecks_FollowSysLanguageAndBcWarehouse()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<IUserProfileRepository>();
        var unused = await uow.Transaction.Connection!.ExecuteScalarAsync<long>(
            "INSERT INTO bc_warehouse (warehouse_code, warehouse_name, use_yn, created_by, updated_by) VALUES ('PROF3', 'Old', false, 't', 't') RETURNING id",
            transaction: uow.Transaction);
        var main = await uow.Transaction.Connection!.ExecuteScalarAsync<long>(
            "SELECT id FROM bc_warehouse WHERE warehouse_code = 'MAIN'", transaction: uow.Transaction);

        (await repository.IsLanguageInUseAsync("ko", uow.Transaction, None)).Should().BeTrue();
        (await repository.IsLanguageInUseAsync("sw", uow.Transaction, None)).Should().BeFalse();
        (await repository.IsLanguageInUseAsync("xx", uow.Transaction, None)).Should().BeFalse();
        (await repository.IsWarehouseInUseAsync(main, uow.Transaction, None)).Should().BeTrue();
        (await repository.IsWarehouseInUseAsync(unused, uow.Transaction, None)).Should().BeFalse();
        (await repository.IsWarehouseInUseAsync(-1, uow.Transaction, None)).Should().BeFalse();
    }

    private async Task<long> CreateUserAsync(string userId)
    {
        await AdminAccount.CreateOrResetAsync(db.ConnectionString, userId, "Pass-1", None);
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        return (await db.Get<IAppUserRepository>().FindByUserIdAsync(userId, uow.Transaction, None))!.Id;
    }
}
