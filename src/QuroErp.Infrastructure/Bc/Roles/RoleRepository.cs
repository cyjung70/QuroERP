using System.Data;
using Dapper;
using QuroErp.Application.Bc.Roles;
using QuroErp.Contracts.Bc.Roles;

namespace QuroErp.Infrastructure.Bc.Roles;

internal sealed class RoleRepository : IRoleRepository
{
    private const string SelectInUse = """
        SELECT id, role_code FROM sys_role WHERE use_yn ORDER BY sort_order NULLS LAST, role_code
        """;

    public async Task<IReadOnlyList<RoleDto>> FindInUseAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<RoleDto>(
            new CommandDefinition(SelectInUse, transaction: transaction, cancellationToken: cancellationToken))).ToList();
}
