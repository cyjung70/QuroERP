using System.Data;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Application.Common.Auth;

public interface IAuthSettingRepository
{
    /// <summary>The one setting row, or null if the row is missing (callers then use the defaults).</summary>
    Task<AuthSetting?> GetAsync(IDbTransaction transaction, CancellationToken cancellationToken);
}
