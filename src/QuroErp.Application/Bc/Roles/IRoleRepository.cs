using System.Data;
using QuroErp.Contracts.Bc.Roles;

namespace QuroErp.Application.Bc.Roles;

public interface IRoleRepository
{
    /// <summary>Roles in use (use_yn), in sort order, then role code order.</summary>
    Task<IReadOnlyList<RoleDto>> FindInUseAsync(IDbTransaction transaction, CancellationToken cancellationToken);
}
