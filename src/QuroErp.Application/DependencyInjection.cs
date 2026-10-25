using Microsoft.Extensions.DependencyInjection;
using QuroErp.Application.Common.Auth;

namespace QuroErp.Application;

public static class DependencyInjection
{
    /// <summary>Application 서비스를 등록한다. TimeProvider와 Repository 구현은 호스트·Infrastructure가 등록한다.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<LoginService>();
        services.AddScoped<ApiKeyAuthenticator>();
        return services;
    }
}
