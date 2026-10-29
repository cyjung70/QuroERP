using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Bc.Roles;

namespace QuroErp.Application.Bc.Roles;

/// <summary>Roles in use for role select fields. Menu assignment comes with the role-menu task (TASK-BC-009).</summary>
public sealed class RoleService(IRoleRepository roles, IUnitOfWorkFactory unitOfWork)
{
    public async Task<IReadOnlyList<RoleDto>> GetInUseAsync(CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var list = await roles.FindInUseAsync(uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return list;
    }
}
