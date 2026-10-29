using Dapper;
using FluentAssertions;
using QuroErp.Application.Bc.Roles;
using QuroErp.Application.Common.Persistence;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Bc.Roles;

/// <summary>Roles in use on real PostgreSQL. The test rolls back.</summary>
[Collection(AuthSchemaDefinition.Name)]
public class RoleRepositoryTests(AuthSchemaFixture db)
{
    private static readonly CancellationToken None = CancellationToken.None;

    [DatabaseFact, Trait("Id", "BC-UT-122")]
    public async Task InUse_LeavesOutRolesNotInUse_AndFollowsSortOrder()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        await uow.Transaction.Connection!.ExecuteAsync(
            """
            INSERT INTO sys_role (role_code, sort_order, use_yn, created_by, updated_by) VALUES ('zz_first', -1, true, 't', 't'), ('zz_retired', -2, false, 't', 't')
            """,
            transaction: uow.Transaction);

        var roles = await db.Get<IRoleRepository>().FindInUseAsync(uow.Transaction, None);

        roles[0].RoleCode.Should().Be("zz_first");
        roles.Should().Contain(r => r.RoleCode == "admin").And.NotContain(r => r.RoleCode == "zz_retired");
    }
}
