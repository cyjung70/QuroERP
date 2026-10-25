using System.Data.Common;

namespace QuroErp.Infrastructure.Common.Persistence;

/// <summary>열린 DB 연결을 만든다. 트랜잭션 없이 읽는 Repository 조회에 쓴다.</summary>
public interface IDbConnectionFactory
{
    Task<DbConnection> OpenAsync(CancellationToken cancellationToken);
}
