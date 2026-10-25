using System.Data;
using Dapper;
using QuroErp.Application.Common.Auth;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Infrastructure.Common.Auth;

internal sealed class ApiClientRepository : IApiClientRepository
{
    private const string SelectByKeyHash = """
        SELECT c.id, c.client_code, r.role_code, c.revoked_at
        FROM sys_api_client c
        JOIN sys_role r ON r.id = c.role_id
        WHERE c.key_hash = @KeyHash
        """;

    public Task<ApiClient?> FindByKeyHashAsync(string keyHash, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.QuerySingleOrDefaultAsync<ApiClient>(
            new CommandDefinition(SelectByKeyHash, new { KeyHash = keyHash }, transaction, cancellationToken: cancellationToken));
}
