using System.Data;

namespace QuroErp.Application.Common.Persistence;

/// <summary>
/// 한 유스케이스의 트랜잭션 (layers.md "트랜잭션": 한 유스케이스 = 한 서비스 메서드 = 한 트랜잭션).
/// <see cref="CommitAsync"/> 없이 해제하면 롤백한다.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    /// <summary>Repository 메서드에 넘기는 트랜잭션.</summary>
    IDbTransaction Transaction { get; }

    Task CommitAsync(CancellationToken cancellationToken);

    Task RollbackAsync(CancellationToken cancellationToken);
}
