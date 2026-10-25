using System.Data;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Application.Common.Auth;

public interface IUserLoginLogRepository
{
    Task InsertAsync(UserLoginLog log, IDbTransaction transaction, CancellationToken cancellationToken);
}
