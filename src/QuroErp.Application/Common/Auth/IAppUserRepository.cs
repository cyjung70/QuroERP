using System.Data;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Application.Common.Auth;

public interface IAppUserRepository
{
    Task<AppUser?> FindByUserIdAsync(string userId, IDbTransaction transaction, CancellationToken cancellationToken);

    Task<AppUser?> FindByIdAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken);
}
