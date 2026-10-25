using System.Data;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Application.Common.Auth;

public interface IApiClientRepository
{
    /// <summary>키 해시로 클라이언트를 찾는다. 폐기 여부는 거르지 않는다.</summary>
    Task<ApiClient?> FindByKeyHashAsync(string keyHash, IDbTransaction transaction, CancellationToken cancellationToken);
}
