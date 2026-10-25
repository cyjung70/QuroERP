using System.Data;
using System.Data.Common;
using QuroErp.Application.Common.Persistence;

namespace QuroErp.Infrastructure.Common.Persistence;

/// <summary>연결 하나와 트랜잭션 하나를 가진다. 해제할 때 커밋하지 않았으면 롤백하고 연결을 닫는다.</summary>
internal sealed class UnitOfWork : IUnitOfWork
{
    private readonly DbConnection _connection;
    private readonly DbTransaction _transaction;
    private bool _completed;

    private UnitOfWork(DbConnection connection, DbTransaction transaction)
    {
        _connection = connection;
        _transaction = transaction;
    }

    public IDbTransaction Transaction => _transaction;

    public static async Task<UnitOfWork> BeginAsync(IDbConnectionFactory connections, CancellationToken cancellationToken)
    {
        var connection = await connections.OpenAsync(cancellationToken);
        try
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken);
            return new UnitOfWork(connection, transaction);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        EnsureActive();
        await _transaction.CommitAsync(cancellationToken);
        _completed = true;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        EnsureActive();
        await _transaction.RollbackAsync(cancellationToken);
        _completed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_completed)
        {
            await _transaction.RollbackAsync();
            _completed = true;
        }
        await _transaction.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private void EnsureActive()
    {
        if (_completed)
        {
            throw new InvalidOperationException("이미 커밋 또는 롤백한 UnitOfWork다.");
        }
    }
}

internal sealed class UnitOfWorkFactory(IDbConnectionFactory connections) : IUnitOfWorkFactory
{
    public async Task<IUnitOfWork> BeginAsync(CancellationToken cancellationToken) =>
        await UnitOfWork.BeginAsync(connections, cancellationToken);
}
