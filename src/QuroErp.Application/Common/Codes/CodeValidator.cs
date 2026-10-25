using QuroErp.Application.Common.Persistence;
using QuroErp.Domain.Common;

namespace QuroErp.Application.Common.Codes;

/// <summary>Runs before the save transaction (quro-api step 3.1), so it uses its own unit of work.</summary>
public sealed class CodeValidator(ICodeRepository codes, IUnitOfWorkFactory unitOfWork) : ICodeValidator
{
    public async Task EnsureValidAsync(string majorCode, string? value, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var exists = await codes.ExistsActiveAsync(majorCode, value, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        if (!exists)
        {
            throw new DomainException(CodeMessages.InvalidCode, majorCode, value);
        }
    }
}
