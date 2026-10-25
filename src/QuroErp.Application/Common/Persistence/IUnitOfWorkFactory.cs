namespace QuroErp.Application.Common.Persistence;

/// <summary>DB 연결을 열고 트랜잭션을 시작한다.</summary>
public interface IUnitOfWorkFactory
{
    Task<IUnitOfWork> BeginAsync(CancellationToken cancellationToken);
}
