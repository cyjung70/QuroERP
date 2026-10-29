using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuroErp.Application.Bc.Companies;
using QuroErp.Application.Bc.Items;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Common.Auth;
using QuroErp.Application.Common.Codes;
using QuroErp.Application.Common.DocumentNumbers;
using QuroErp.Application.Common.I18n;
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Mm.Inventory;
using QuroErp.Infrastructure.Bc.Companies;
using QuroErp.Infrastructure.Bc.Items;
using QuroErp.Infrastructure.Bc.Localization;
using QuroErp.Infrastructure.Common.Auth;
using QuroErp.Infrastructure.Common.Codes;
using QuroErp.Infrastructure.Common.DocumentNumbers;
using QuroErp.Infrastructure.Common.I18n;
using QuroErp.Infrastructure.Common.Persistence;
using QuroErp.Infrastructure.Mm.Inventory;

namespace QuroErp.Infrastructure;

public static class DependencyInjection
{
    /// <summary>연결 문자열 이름. 설정 키 ConnectionStrings:QuroErp (QuroERP.Database와 같다).</summary>
    public const string ConnectionStringName = "QuroErp";

    /// <summary>Infrastructure 서비스를 등록한다. 연결 문자열이 없으면 예외를 던진다.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"연결 문자열 ConnectionStrings:{ConnectionStringName}이 없다. user-secrets나 환경변수 ConnectionStrings__{ConnectionStringName}로 설정한다.");
        }

        // database.md "이름": snake_case 컬럼 → PascalCase 속성
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        services.AddNpgsqlDataSource(connectionString);
        services.AddSingleton<IDbConnectionFactory, NpgsqlConnectionFactory>();
        services.AddSingleton<IUnitOfWorkFactory, UnitOfWorkFactory>();
        services.AddSingleton<IAppUserRepository, AppUserRepository>();
        services.AddSingleton<IUserLoginLogRepository, UserLoginLogRepository>();
        services.AddSingleton<IAuthSettingRepository, AuthSettingRepository>();
        services.AddSingleton<IAutoLoginTokenRepository, AutoLoginTokenRepository>();
        services.AddSingleton<IApiClientRepository, ApiClientRepository>();
        services.AddSingleton<IPasswordHashService, PasswordHashService>();
        services.AddSingleton<ITranslationRepository, TranslationRepository>();
        services.AddSingleton<ICodeRepository, CodeRepository>();
        services.AddSingleton<ICompanyRepository, CompanyRepository>();
        services.AddSingleton<ILocalizationRepository, LocalizationRepository>();
        services.AddSingleton<ILocalizationSettingRepository, LocalizationSettingRepository>();
        services.AddSingleton<IInventorySettingRepository, InventorySettingRepository>();
        services.AddSingleton<IItemRepository, ItemRepository>();
        services.AddSingleton<IDocumentNumberRepository, DocumentNumberRepository>();
        services.AddSingleton<IInventoryRepository, InventoryRepository>();
        return services;
    }
}
