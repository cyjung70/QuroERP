using System.Data.Common;
using Npgsql;

namespace QuroErp.Infrastructure.Common.Persistence;

internal sealed class NpgsqlConnectionFactory(NpgsqlDataSource dataSource) : IDbConnectionFactory
{
    public async Task<DbConnection> OpenAsync(CancellationToken cancellationToken) =>
        await dataSource.OpenConnectionAsync(cancellationToken);
}
