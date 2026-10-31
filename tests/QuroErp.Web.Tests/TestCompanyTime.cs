using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using QuroErp.Web.Common;

namespace QuroErp.Web.Tests;

/// <summary>The company time zone formatter of the screens, with a fixed time zone instead of the localization API.</summary>
public static class TestCompanyTime
{
    public static IServiceCollection AddCompanyTime(this IServiceCollection services, string timeZone = "Asia/Seoul") =>
        services.AddSingleton(new CompanyTime(_ => Task.FromResult(timeZone), new MemoryCache(new MemoryCacheOptions())));
}
