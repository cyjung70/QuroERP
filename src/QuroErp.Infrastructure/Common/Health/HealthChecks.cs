using Dapper;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Common.Printing;

namespace QuroErp.Infrastructure.Common.Health;

/// <summary>The database answers a query.</summary>
public sealed class DatabaseHealthCheck(IUnitOfWorkFactory unitOfWork) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        await uow.Transaction.Connection!.ExecuteScalarAsync<int>(
            new CommandDefinition("SELECT 1", transaction: uow.Transaction, cancellationToken: cancellationToken));
        await uow.CommitAsync(cancellationToken);
        return HealthCheckResult.Healthy();
    }
}

/// <summary>Chromium starts and opens a page, so PDF printing works (reports.md "배포").</summary>
public sealed class ChromiumHealthCheck(IPdfRenderer pdf) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await pdf.CheckAsync(cancellationToken);
        return HealthCheckResult.Healthy();
    }
}
