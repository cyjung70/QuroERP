using QuroErp.Application.Common.Persistence;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Application.Common.Auth;

/// <summary>외부 서버의 API 키를 확인한다 (security.md "인증 경로").</summary>
public sealed class ApiKeyAuthenticator(IApiClientRepository clients, IUnitOfWorkFactory unitOfWork, TimeProvider clock)
{
    /// <returns>쓸 수 있는 키면 클라이언트, 아니면 null.</returns>
    public async Task<ApiClient?> AuthenticateAsync(string apiKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var client = await clients.FindByKeyHashAsync(ApiKeyHash.Compute(apiKey), uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return client is not null && client.IsActive(clock.GetUtcNow()) ? client : null;
    }
}
