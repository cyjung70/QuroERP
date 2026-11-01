using System.Collections.Concurrent;
using System.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QuroErp.Application.Bc.BusinessPlaces;
using QuroErp.Application.Bc.Codes;
using QuroErp.Application.Bc.Companies;
using QuroErp.Application.Bc.FieldPolicies;
using QuroErp.Contracts.Bc.FieldPolicies;
using QuroErp.Domain.Bc.FieldPolicies;
using QuroErp.Contracts.Bc.BusinessPlaces;
using QuroErp.Contracts.Bc.Companies;
using QuroErp.Application.Bc.Items;
using QuroErp.Api.Tests.Bc.Localization;
using QuroErp.Api.Tests.Bc.Tax;
using QuroErp.Application.Bc.Localization;
using QuroErp.Api.Tests.Bc.Codes;
using QuroErp.Api.Tests.Bc.Menus;
using QuroErp.Api.Tests.Bc.Roles;
using QuroErp.Api.Tests.Bc.Users;
using QuroErp.Application.Bc.UserProfiles;
using QuroErp.Application.Bc.Users;
using QuroErp.Application.Common.Auth;
using QuroErp.Application.Common.Codes;
using QuroErp.Application.Common.I18n;
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Mm.Inventory;
using QuroErp.Application.Bc.Menus;
using QuroErp.Application.Bc.Roles;
using QuroErp.Application.Bc.Tax;
using QuroErp.Application.Mm.Warehouses;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Domain.Bc.BusinessPlaces;
using QuroErp.Contracts.Mm.Warehouses;
using QuroErp.Domain.Bc.Companies;
using QuroErp.Domain.Bc.Items;
using QuroErp.Domain.Bc.UserProfiles;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Api.Tests;

/// <summary>
/// Api 호스트를 메모리에서 띄운다. DB 대신 가짜 Repository를 쓰고, 비밀번호 해시·JWT·인증 미들웨어는 실제 구성을 쓴다.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string UserId = "kim";
    public const string Password = "Correct-Pass-1";
    public const string ApiKey = "test-api-key-0123456789";
    public const string RevokedApiKey = "revoked-api-key-0123456789";

    /// <summary>User for lock tests, so the admin user above never gets locked.</summary>
    public const string LockUserId = "lock";

    /// <summary>User who must change the password (first login).</summary>
    public const string NewUserId = "newbie";

    /// <summary>Users whose password the password change tests change. Each test uses its own.</summary>
    public const string ChangeUserId = "changer";
    public const string ForcedChangeUserId = "forced";
    public const string StaleUserId = "stale";

    /// <summary>User whose info the my info tests change (BC-UC-003).</summary>
    public const string ProfileUserId = "profile";
    public const long ProfileUserKey = 16;

    /// <summary>Requests per minute per IP on /api/auth/*. High here; the rate limit test lowers it to the real 10.</summary>
    public const string TestRateLimit = "1000";

    /// <summary>Every log message the host wrote, at every level (no raw password or token may appear).</summary>
    public ConcurrentQueue<string> LogMessages { get; } = new();

    public AuthSetting AuthSetting { get; set; } = new();

    public DateTimeOffset AuthSettingUpdatedAt { get; set; } = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);

    public InMemoryAutoLoginTokenRepository AutoLoginTokens { get; } = new();

    public ConcurrentQueue<UserLoginLog> LoginLogs { get; } = new();

    public InMemoryPasswordHistoryRepository PasswordHistory { get; } = new();

    /// <summary>Company logo state; there is one company and no logo at start.</summary>
    public InMemoryCompanyRepository Companies { get; } = new();

    /// <summary>The default business place MAIN (BC-BR-319) with a valid KR registration number.</summary>
    public InMemoryBusinessPlaceRepository BusinessPlaces { get; } = new();

    /// <summary>Field policy company overrides (bc_field_policy); none at start. The country packs are the built-in ones.</summary>
    public InMemoryFieldPolicyRepository FieldPolicies { get; } = new();

    /// <summary>Localization state: a KR company (default language ko), languages en/ko (sw off), KRW and USD.</summary>
    public InMemoryLocalizationRepository Localization { get; } = new();

    /// <summary>Applies localization setting writes to <see cref="Localization"/>. Documents exist for none of the modules.</summary>
    public InMemoryLocalizationSettingRepository LocalizationSettings => _localizationSettings ??= new(Localization);

    private InMemoryLocalizationSettingRepository? _localizationSettings;

    /// <summary>Items; empty at start. Ids in <see cref="InMemoryItemRepository.InUse"/> count as used (MM-BR-807).</summary>
    public InMemoryItemRepository Items { get; } = new();

    /// <summary>Warehouse choice list the API returns as is (the repository filters and sorts).</summary>
    public List<WarehouseOptionDto> WarehouseOptions { get; } = [new(1, "MAIN", "Main"), new(2, "W02", "Second")];

    /// <summary>Roles, menus and role menu assignments (BC-UC-005).</summary>
    public InMemoryRoleRepository Roles { get; } = new();

    /// <summary>Company menus (BC-UC-006).</summary>
    public InMemoryMenuRepository Menus { get; } = new();

    /// <summary>Minor codes of code management (BC-UC-008). Their names come through the translation fake.</summary>
    public InMemoryCodeMinorRepository CodeMinors { get; } = new();

    /// <summary>Major codes of Major code management (BC-UC-023). Their names come through the translation fake.</summary>
    public InMemoryCodeMajorRepository CodeMajors { get; } = new();

    /// <summary>Currencies of currency management (BC-UC-026). Their names come through the translation fake.</summary>
    public InMemoryCurrencyRepository Currencies { get; } = new();

    public InMemoryExchangeRateRepository ExchangeRates { get; } = new();

    public InMemoryTaxCodeRepository TaxCodes { get; } = new();

    /// <summary>My info rows of kim (7) and profile (16). Languages ko and en and warehouse 1 are in use.</summary>
    public InMemoryUserProfileRepository UserProfiles { get; } = new();

    /// <summary>Users of user management (BC-UC-004).</summary>
    public InMemoryUserRepository Users { get; } = new();

    /// <summary>Inventory setting state; DENY at start.</summary>
    public InMemoryInventorySettingRepository InventorySettings { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // 연결은 열지 않는다 (Repository를 가짜로 바꾼다)
        builder.UseSetting("ConnectionStrings:QuroErp", "Host=localhost;Database=unused");
        builder.UseSetting("Jwt:SigningKey", "test-signing-key-0123456789-abcdefghij");
        builder.UseSetting("Auth:RateLimit:PerMinute", TestRateLimit);
        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(new CollectingLoggerProvider(LogMessages)));

        builder.ConfigureTestServices(services =>
        {
            var hash = new PasswordHasher<AppUser>().HashPassword(new AppUser(), Password);
            AppUser[] users =
            [
                new() { Id = 7, UserId = UserId, UserName = "Kim", RoleCode = "admin", ValidFlag = "Y", PasswordHash = hash },
                new() { Id = 11, UserId = LockUserId, RoleCode = "admin", ValidFlag = "Y", PasswordHash = hash },
                new() { Id = 12, UserId = NewUserId, RoleCode = "admin", ValidFlag = "Y", PasswordHash = hash, MustChangePassword = true },
                new() { Id = 13, UserId = ChangeUserId, RoleCode = "admin", ValidFlag = "Y", PasswordHash = hash },
                new() { Id = 14, UserId = ForcedChangeUserId, RoleCode = "admin", ValidFlag = "Y", PasswordHash = hash, MustChangePassword = true },
                new() { Id = 15, UserId = StaleUserId, RoleCode = "admin", ValidFlag = "Y", PasswordHash = hash },
                new() { Id = ProfileUserKey, UserId = ProfileUserId, RoleCode = "user", ValidFlag = "Y", PasswordHash = hash },
            ];
            services.AddSingleton<IUnitOfWorkFactory, FakeUnitOfWorkFactory>();
            services.AddSingleton<IAppUserRepository>(new FakeAppUserRepository(users));
            services.AddSingleton<IAuthSettingRepository>(new FakeAuthSettingRepository(this));
            services.AddSingleton<IAutoLoginTokenRepository>(AutoLoginTokens);
            services.AddSingleton<IPasswordHistoryRepository>(PasswordHistory);
            services.AddSingleton<IUserLoginLogRepository>(new FakeUserLoginLogRepository(LoginLogs));
            services.AddSingleton<ICodeRepository, FakeCodeRepository>();
            services.AddSingleton<ITranslationRepository>(new FakeTranslationRepository(CodeMinors, CodeMajors, Currencies, TaxCodes));
            services.AddSingleton<ICompanyRepository>(Companies);
            services.AddSingleton<IBusinessPlaceRepository>(BusinessPlaces);
            services.AddSingleton<IFieldPolicyRepository>(FieldPolicies);
            services.AddSingleton<ILocalizationRepository>(Localization);
            services.AddSingleton<ILocalizationSettingRepository>(LocalizationSettings);
            services.AddSingleton<IInventorySettingRepository>(InventorySettings);
            services.AddSingleton<IItemRepository>(Items);
            services.AddSingleton<IWarehouseRepository>(new FakeWarehouseRepository(this));
            services.AddSingleton<IRoleRepository>(Roles);
            services.AddSingleton<IMenuRepository>(Menus);
            services.AddSingleton<ICodeMinorRepository>(CodeMinors);
            services.AddSingleton<ICodeMajorRepository>(CodeMajors);
            services.AddSingleton<ICurrencyRepository>(Currencies);
            services.AddSingleton<IExchangeRateRepository>(ExchangeRates);
            services.AddSingleton<ITaxCodeRepository>(TaxCodes);
            services.AddSingleton<IUserProfileRepository>(UserProfiles);
            services.AddSingleton<IUserRepository>(Users);
            services.AddControllers().AddApplicationPart(typeof(ApiFactory).Assembly);
            services.AddSingleton<IApiClientRepository>(new FakeApiClientRepository(new Dictionary<string, ApiClient>
            {
                [ApiKeyHash.Compute(ApiKey)] = new() { ClientCode = "edi", RoleCode = "manager" },
                [ApiKeyHash.Compute(RevokedApiKey)] = new() { ClientCode = "old", RoleCode = "manager", RevokedAt = DateTimeOffset.UnixEpoch },
            }));
        });
    }

    private sealed class FakeUnitOfWorkFactory : IUnitOfWorkFactory
    {
        public Task<IUnitOfWork> BeginAsync(CancellationToken cancellationToken) => Task.FromResult<IUnitOfWork>(new FakeUnitOfWork());

        private sealed class FakeUnitOfWork : IUnitOfWork
        {
            public IDbTransaction Transaction => null!;

            public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            public Task RollbackAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class FakeAppUserRepository(AppUser[] users) : IAppUserRepository
    {
        public Task<AppUser?> FindByUserIdAsync(string userId, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(users.SingleOrDefault(u => u.UserId == userId));

        public Task<AppUser?> FindByIdAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(users.SingleOrDefault(u => u.Id == id));

        public Task<AppUser?> LockByUserIdAsync(string userId, IDbTransaction transaction, CancellationToken cancellationToken) =>
            FindByUserIdAsync(userId, transaction, cancellationToken);

        public Task<AppUser?> LockByIdAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
            FindByIdAsync(id, transaction, cancellationToken);

        // The users are kept in memory, so their state is already saved
        public Task SaveLoginStateAsync(AppUser user, string? langCode, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task ChangePasswordAsync(
            long id, string passwordHash, DateTimeOffset now, string auditUser, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            var index = Array.FindIndex(users, u => u.Id == id);
            var u = users[index];
            users[index] = new AppUser
            {
                Id = u.Id,
                UserId = u.UserId,
                PasswordHash = passwordHash,
                UserName = u.UserName,
                RoleCode = u.RoleCode,
                ValidFlag = u.ValidFlag,
                LangCode = u.LangCode,
                PasswordChangedAt = now,
                TokenVersion = u.TokenVersion + 1,
            };
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuthMenu>> FindMenusAsync(long appUserId, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AuthMenu>>([new AuthMenu("BC01", "Company", "bc-company", "BC", null, "Y", "P")]);

        public Task<string?> FindCompanyNameAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<string?>("Quro Test Co.");

        public Task<AuthWarehouse?> FindDefaultWarehouseAsync(long appUserId, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<AuthWarehouse?>(new AuthWarehouse(1, "W01", "Main"));
    }

    private sealed class FakeWarehouseRepository(ApiFactory factory) : IWarehouseRepository
    {
        public Task<IReadOnlyList<WarehouseOptionDto>> FindOptionsAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WarehouseOptionDto>>(factory.WarehouseOptions);
    }

    private sealed class FakeAuthSettingRepository(ApiFactory factory) : IAuthSettingRepository
    {
        public Task<AuthSetting?> GetAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<AuthSetting?>(factory.AuthSetting);

        public Task<AuthSettingRow?> FindForEditAsync(bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<AuthSettingRow?>(new AuthSettingRow(factory.AuthSetting, factory.AuthSettingUpdatedAt));

        public Task<bool> UpdateAsync(
            AuthSetting setting, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            if (expectedUpdatedAt != factory.AuthSettingUpdatedAt)
            {
                return Task.FromResult(false);
            }
            factory.AuthSetting = setting;
            factory.AuthSettingUpdatedAt = factory.AuthSettingUpdatedAt.AddMinutes(1);
            return Task.FromResult(true);
        }
    }

    /// <summary>sys_password_history in memory, oldest first.</summary>
    public sealed class InMemoryPasswordHistoryRepository : IPasswordHistoryRepository
    {
        private readonly ConcurrentQueue<(long AppUserId, string PasswordHash)> _rows = new();

        public Task<IReadOnlyList<string>> FindRecentAsync(long appUserId, int count, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(_rows.Where(r => r.AppUserId == appUserId).Select(r => r.PasswordHash).Reverse().Take(count).ToList());

        public Task InsertAsync(long appUserId, string passwordHash, string auditUser, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            _rows.Enqueue((appUserId, passwordHash));
            return Task.CompletedTask;
        }
    }

    public sealed class InMemoryAutoLoginTokenRepository : IAutoLoginTokenRepository
    {
        public ConcurrentDictionary<string, AutoLoginTokenRow> Tokens { get; } = new();

        public Task<AutoLoginTokenRow?> FindByHashAsync(
            string tokenHash, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(Tokens.GetValueOrDefault(tokenHash));

        public Task InsertAsync(
            long appUserId, string tokenHash, DateTimeOffset expiresAt, string? userAgent, DateTimeOffset now,
            IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Tokens[tokenHash] = new AutoLoginTokenRow(Tokens.Count + 1, appUserId, expiresAt, null, now);
            return Task.CompletedTask;
        }

        public Task RevokeAsync(long appUserId, string tokenHash, DateTimeOffset now, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            if (Tokens.TryGetValue(tokenHash, out var token) && token.AppUserId == appUserId && token.RevokedAt is null)
            {
                Tokens[tokenHash] = token with { RevokedAt = now };
            }
            return Task.CompletedTask;
        }

        public Task RevokeAllAsync(long appUserId, DateTimeOffset now, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            foreach (var (hash, token) in Tokens.Where(t => t.Value.AppUserId == appUserId && t.Value.RevokedAt is null))
            {
                Tokens[hash] = token with { RevokedAt = now };
            }
            return Task.CompletedTask;
        }

        public Task RevokeEveryUserAsync(DateTimeOffset now, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            foreach (var (hash, token) in Tokens.Where(t => t.Value.RevokedAt is null))
            {
                Tokens[hash] = token with { RevokedAt = now };
            }
            return Task.CompletedTask;
        }
    }

    private sealed class CollectingLoggerProvider(ConcurrentQueue<string> messages) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CollectingLogger(messages);

        public void Dispose()
        {
        }

        private sealed class CollectingLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                messages.Enqueue(formatter(state, exception) + exception);
        }
    }

    private sealed class FakeUserLoginLogRepository(ConcurrentQueue<UserLoginLog> logs) : IUserLoginLogRepository
    {
        public Task InsertAsync(UserLoginLog log, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            logs.Enqueue(log);
            return Task.CompletedTask;
        }
    }

    /// <summary>VALID_FLAG = Y, N, S (S has no translation). Item codes: ITEM_TYPE, UNIT, ITEM_CLASS, GRADE, DISPLAY_OPTIONS.</summary>
    private sealed class FakeCodeRepository : ICodeRepository
    {
        private static readonly List<CodeMinorRow> ValidFlag = [new(1, "Y", 10), new(2, "N", 20), new(3, "S", 30)];

        private static readonly Dictionary<string, string[]> ItemCodes = new()
        {
            ["ITEM_TYPE"] = ["INVENTORY", "SET", "SERVICE"],
            ["UNIT"] = ["EA", "PCS"],
            ["ITEM_CLASS"] = ["PART", "ZZNAV1", "ZZNAV2", "ZZAUD1", "ZZAUD2", "ZZBC1", "ZZBC2", "ZZBC3", "ZZBC4", "ZZBC5"],
            ["GRADE"] = ["A01"],
            ["DISPLAY_OPTIONS"] = ["Y", "N", "ALL"],
        };

        public Task<IReadOnlyList<CodeMinorRow>?> FindActiveMinorsAsync(
            string majorCode, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CodeMinorRow>?>(majorCode == "VALID_FLAG" ? ValidFlag : null);

        public Task<bool> ExistsActiveAsync(string majorCode, string minorCode, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(majorCode == "VALID_FLAG"
                ? ValidFlag.Any(m => m.MinorCode == minorCode)
                : ItemCodes.TryGetValue(majorCode, out var minors) && minors.Contains(minorCode));
    }

    private sealed class FakeTranslationRepository(
        InMemoryCodeMinorRepository codeMinors, InMemoryCodeMajorRepository codeMajors, InMemoryCurrencyRepository currencies,
        InMemoryTaxCodeRepository taxCodes) : ITranslationRepository
    {
        private static readonly List<TranslationRow> Rows =
        [
            new(1, "ko", "사용"), new(1, "en", "Used"),
            new(2, "ko", "사용 안 함"), new(2, "en", "Not Used"),
        ];

        public Task<IReadOnlyList<TranslationRow>> FindAsync(
            TranslationTarget target, IReadOnlyCollection<long> ownerIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TranslationRow>>(Rows.Where(r => ownerIds.Contains(r.OwnerId))
                .Concat(target == TranslationTarget.CodeMinor ? codeMinors.NamesOf(ownerIds) : [])
                .Concat(target == TranslationTarget.CodeMajor ? codeMajors.NamesOf(ownerIds) : [])
                .Concat(target == TranslationTarget.CurrencyName ? currencies.NamesOf(ownerIds) : [])
                .Concat(target == TranslationTarget.TaxCodeName ? taxCodes.NamesOf(ownerIds) : [])
                .ToList());
    }

    private sealed class FakeApiClientRepository(Dictionary<string, ApiClient> clients) : IApiClientRepository
    {
        public Task<ApiClient?> FindByKeyHashAsync(string keyHash, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(clients.GetValueOrDefault(keyHash));
    }

    /// <summary>A KR company (KRW, ko) with no logo and no trading data at start.</summary>
    public sealed class InMemoryCompanyRepository : ICompanyRepository
    {
        public static readonly DateTimeOffset Created = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);

        public CompanyDto? Company { get; set; } = new(
            "Quro", null, null, null, null, null, null, null, "KR", "KRW", "ko", "Asia/Seoul", "WITH_SHIPMENT", false, Created, "setup", Created, "setup");

        public CompanyLogoRow? Logo { get; set; }

        public string? UpdatedBy { get; private set; }

        public bool HasTransactions { get; set; }

        public bool HasUnbilledShipments { get; set; }

        public Task<CompanyDto?> FindAsync(bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(Company is null ? null : Company with { HasLogo = Logo is not null });

        public Task<bool> UpdateAsync(
            CompanyFields fields, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            if (Company is null || Company.UpdatedAt != expectedUpdatedAt)
            {
                return Task.FromResult(false);
            }
            Company = new CompanyDto(
                fields.Name!, fields.Billto, fields.Shipto, fields.Contact, fields.TelNo, fields.FaxNo, fields.Represent, fields.Email,
                fields.CountryCode!, fields.BaseCurrencyCode!, fields.DefaultLangCode!, fields.TimeZone!,
                fields.BillingMode!, false, Company.CreatedAt, Company.CreatedBy, Company.UpdatedAt.AddMinutes(1), updatedBy);
            UpdatedBy = updatedBy;
            return Task.FromResult(true);
        }

        public Task<bool> HasTransactionsAsync(IDbTransaction transaction, CancellationToken cancellationToken) => Task.FromResult(HasTransactions);

        public Task<bool> HasUnbilledShipmentsAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(HasUnbilledShipments);

        public Task<CompanyLogoRow?> FindLogoAsync(IDbTransaction transaction, CancellationToken cancellationToken) => Task.FromResult(Logo);

        public Task<bool> UpdateLogoAsync(
            byte[] image, string contentType, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Logo = new CompanyLogoRow(image, contentType);
            UpdatedBy = updatedBy;
            return Task.FromResult(true);
        }

        public Task<bool> DeleteLogoAsync(string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Logo = null;
            UpdatedBy = updatedBy;
            return Task.FromResult(Company is not null);
        }
    }

    public sealed class InMemoryFieldPolicyRepository : IFieldPolicyRepository
    {
        public static readonly DateTimeOffset Saved = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

        public List<FieldPolicyOverrideDto> Rows { get; } = [];

        public Task<IReadOnlyList<FieldPolicyOverrideDto>> FindAllAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FieldPolicyOverrideDto>>([.. Rows.OrderBy(r => r.FieldKey, StringComparer.Ordinal)]);

        public Task UpsertAsync(string fieldKey, FieldPolicyEntry entry, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Rows.RemoveAll(r => r.FieldKey == fieldKey);
            Rows.Add(new(Rows.Count + 1, fieldKey, entry.State, entry.DefaultValue, entry.LabelKey, Saved, updatedBy));
            return Task.CompletedTask;
        }

        public Task DeleteAsync(IReadOnlyCollection<string> fieldKeys, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Rows.RemoveAll(r => fieldKeys.Contains(r.FieldKey));
            return Task.CompletedTask;
        }
    }

    public sealed class InMemoryBusinessPlaceRepository : IBusinessPlaceRepository
    {
        public static readonly DateTimeOffset Created = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);

        public List<BusinessPlaceDto> Places { get; } =
        [
            new(1, "MAIN", "Quro", true, "2208162517", null, null, null, null, null, null, null, null, null, null, true,
                Created, "system", Created, "system"),
        ];

        public Task<IReadOnlyList<BusinessPlaceDto>> ListAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BusinessPlaceDto>>(Places.ToList());

        public Task<BusinessPlaceDto?> FindAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(Places.SingleOrDefault(p => p.Id == id));

        public Task<bool> UpdateAsync(
            long id, BusinessPlaceFields fields, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction,
            CancellationToken cancellationToken)
        {
            var index = Places.FindIndex(p => p.Id == id && p.UpdatedAt == expectedUpdatedAt);
            if (index < 0)
            {
                return Task.FromResult(false);
            }
            var place = Places[index];
            Places[index] = place with
            {
                BusinessPlaceName = fields.BusinessPlaceName!,
                BusinessRegNo = fields.BusinessRegNo,
                SubBusinessNo = fields.SubBusinessNo,
                CeoName = fields.CeoName,
                BusinessType = fields.BusinessType,
                BusinessItem = fields.BusinessItem,
                Addr1 = fields.Addr1,
                Addr2 = fields.Addr2,
                City = fields.City,
                State = fields.State,
                ZipCode = fields.ZipCode,
                TelNo = fields.TelNo,
                UseYn = fields.UseYn,
                UpdatedAt = place.UpdatedAt.AddMinutes(1),
                UpdatedBy = updatedBy,
            };
            return Task.FromResult(true);
        }

        public Task<long?> FindDefaultIdAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(Places.FirstOrDefault(p => p.IsDefault)?.Id);
    }

    public sealed class InMemoryLocalizationRepository : ILocalizationRepository
    {
        public CompanyLocalizationRow? Company { get; set; } = new("KR", "KRW", "ko", "DOCUMENT", "Asia/Seoul");

        public List<LanguageRow> Languages { get; } = [new("en", 1, true), new("ko", 2, true), new("sw", 3, false)];

        public List<CurrencyRow> Currencies { get; } = [new(101, "KRW", 0, true, "KR"), new(102, "USD", 2, true, "US")];

        public List<DecimalSettingRow> DecimalSettings { get; } = [new("MM", 2, "HALF_UP"), new("SD", 0, "DOWN")];

        public List<CountryDecimalRow> CountryDecimals { get; } =
            [new("KR", "MM", 2), new("KR", "SD", 0), new("US", "MM", 4), new("US", "SD", 2)];

        public Task<CompanyLocalizationRow?> FindCompanyAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(Company);

        public Task<IReadOnlyList<LanguageRow>> FindLanguagesAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LanguageRow>>(Languages);

        public Task<IReadOnlyList<CurrencyRow>> FindCurrenciesAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CurrencyRow>>(Currencies);

        public Task<IReadOnlyList<DecimalSettingRow>> FindDecimalSettingsAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DecimalSettingRow>>(DecimalSettings);

        public Task<IReadOnlyList<CountryDecimalRow>> FindCountryDecimalsAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CountryDecimalRow>>(CountryDecimals);
    }

    public sealed class InMemoryLocalizationSettingRepository(InMemoryLocalizationRepository localization) : ILocalizationSettingRepository
    {
        public DateTimeOffset Version { get; private set; } = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);

        public HashSet<string> ModulesWithDocuments { get; } = [];

        public Task<DateTimeOffset> GetVersionAsync(bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(Version);

        public Task<bool> HasDocumentsAsync(string moduleCode, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(ModulesWithDocuments.Contains(moduleCode));

        public Task UpdateLanguagesAsync(IReadOnlyList<LanguageUseRow> languages, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            foreach (var language in languages)
            {
                var index = localization.Languages.FindIndex(l => l.LangCode == language.LangCode);
                localization.Languages[index] = localization.Languages[index] with { UseYn = language.UseYn };
            }
            return Touch();
        }

        public Task UpdateCountryDecimalsAsync(
            IReadOnlyList<CountryDecimalRow> countryDecimals, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            foreach (var row in countryDecimals)
            {
                localization.CountryDecimals[localization.CountryDecimals.FindIndex(d => d.CountryCode == row.CountryCode && d.Module == row.Module)] = row;
            }
            return Touch();
        }

        public Task UpdateDecimalSettingsAsync(
            IReadOnlyList<DecimalSettingRow> settings, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            foreach (var row in settings)
            {
                localization.DecimalSettings[localization.DecimalSettings.FindIndex(d => d.Module == row.Module)] = row;
            }
            return Touch();
        }

        public Task UpdateTaxCalcModeAsync(string taxCalcMode, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            localization.Company = localization.Company! with { TaxCalcMode = taxCalcMode };
            return Touch();
        }

        public Task<int> MoveUserLanguagesAsync(
            IReadOnlyList<string> fromLangCodes, string toLangCode, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        private Task Touch()
        {
            Version = Version.AddSeconds(1);
            return Task.CompletedTask;
        }
    }

    public sealed class InMemoryInventorySettingRepository : IInventorySettingRepository
    {
        public InventorySettingRow Row { get; private set; } = new("DENY", new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero), "system");

        public Task<InventorySettingRow?> GetAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<InventorySettingRow?>(Row);

        public Task<InventorySettingRow?> UpdateAsync(
            string negativeStockMode, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            if (expectedUpdatedAt != Row.UpdatedAt)
            {
                return Task.FromResult<InventorySettingRow?>(null);
            }
            Row = new InventorySettingRow(negativeStockMode, Row.UpdatedAt.AddSeconds(1), updatedBy);
            return Task.FromResult<InventorySettingRow?>(Row);
        }
    }

    public sealed class InMemoryItemRepository : IItemRepository
    {
        private readonly ConcurrentDictionary<long, ItemRow> _rows = new();
        private long _nextId;

        public HashSet<long> InUse { get; } = [];

        public HashSet<long> TypeLocked { get; } = [];

        public Task<ItemRow?> FindAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(_rows.GetValueOrDefault(id));

        public Task<bool> CodeExistsAsync(string itemCd, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(_rows.Values.Any(r => r.ItemCd == itemCd));

        public Task<(IReadOnlyList<ItemRow> Items, int Total)> SearchAsync(
            ItemSearch search, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            var matches = _rows.Values
                .Where(r => search.ItemCd is null || r.ItemCd.StartsWith(search.ItemCd, StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.ItemClass, StringComparer.Ordinal).ThenBy(r => r.ItemCd, StringComparer.Ordinal).ToList();
            return Task.FromResult<(IReadOnlyList<ItemRow>, int)>((matches.Skip(search.Offset).Take(search.Limit).ToList(), matches.Count));
        }

        public Task<long> InsertAsync(Item item, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            var id = Interlocked.Increment(ref _nextId);
            var created = new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
            _rows[id] = ToRow(id, item, created, createdBy, created, createdBy);
            return Task.FromResult(id);
        }

        public Task<bool> UpdateAsync(
            Item item, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            if (!_rows.TryGetValue(item.Id, out var row) || row.UpdatedAt != expectedUpdatedAt)
            {
                return Task.FromResult(false);
            }
            _rows[item.Id] = ToRow(item.Id, item, row.CreatedAt, row.CreatedBy, row.UpdatedAt.AddSeconds(1), updatedBy);
            return Task.FromResult(true);
        }

        public Task<bool> IsTypeLockedAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(TypeLocked.Contains(id));

        public Task<bool> IsInUseAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(InUse.Contains(id));

        public Task<ItemRow?> FindAdjacentAsync(long id, bool forward, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            var ordered = _rows.Values.OrderBy(r => r.ItemClass ?? "", StringComparer.Ordinal).ThenBy(r => r.ItemCd, StringComparer.Ordinal).ToList();
            var index = ordered.FindIndex(r => r.Id == id);
            var target = index < 0 ? -1 : index + (forward ? 1 : -1);
            return Task.FromResult(target >= 0 && target < ordered.Count ? ordered[target] : null);
        }

        public Task<bool> DeleteAsync(long id, DateTimeOffset expectedUpdatedAt, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(_rows.TryGetValue(id, out var row) && row.UpdatedAt == expectedUpdatedAt && _rows.TryRemove(id, out _));

        public Task<IReadOnlyList<ItemBarcodeRow>> ListForBarcodeAsync(
            string itemClass, string? validFlag, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ItemBarcodeRow>>(_rows.Values
                .Where(r => r.ItemClass == itemClass && (validFlag is null || r.ValidFlag == validFlag))
                .OrderBy(r => r.ItemCd, StringComparer.Ordinal)
                .Select(r => new ItemBarcodeRow(r.Id, r.ItemCd, r.ItemName, r.SoUsdPrc, r.UpcCode))
                .ToList());

        public Task<IReadOnlyList<ItemBarcodeOwner>> FindBarcodeOwnersAsync(
            IReadOnlyCollection<string> upcCodes, IReadOnlyCollection<long> exceptIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ItemBarcodeOwner>>(_rows.Values
                .Where(r => r.UpcCode is not null && upcCodes.Contains(r.UpcCode) && !exceptIds.Contains(r.Id))
                .Select(r => new ItemBarcodeOwner(r.UpcCode!, r.ItemCd))
                .ToList());

        public Task SaveBarcodesAsync(
            IReadOnlyList<(long Id, string UpcCode)> barcodes, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            foreach (var (id, upcCode) in barcodes)
            {
                _rows[id] = _rows[id] with { UpcCode = upcCode, UpdatedAt = _rows[id].UpdatedAt.AddSeconds(1) };
            }
            return Task.CompletedTask;
        }

        /// <summary>Stored images by item code.</summary>
        public ConcurrentDictionary<string, byte[]> Images { get; } = new();

        public Task<byte[]?> FindImageAsync(string itemCd, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            if (_rows.Values.FirstOrDefault(r => r.ItemCd == itemCd) is not { } item)
            {
                return Task.FromResult<byte[]?>(null);
            }
            var group = item.ItemGroupCd ?? item.ItemCd;
            var image = _rows.Values
                .Where(r => r.Id == item.Id || (r.ItemGroupCd ?? r.ItemCd) == group)
                .OrderByDescending(r => r.Id == item.Id).ThenByDescending(r => r.Plating is null or "" or "R").ThenBy(r => r.ItemCd, StringComparer.Ordinal)
                .Select(r => Images.GetValueOrDefault(r.ItemCd))
                .FirstOrDefault(i => i is { Length: > 0 });
            return Task.FromResult(image);
        }

        public Task<bool> SaveImageAsync(string itemCd, byte[]? image, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            if (!_rows.Values.Any(r => r.ItemCd == itemCd))
            {
                return Task.FromResult(false);
            }
            if (image is null)
            {
                Images.TryRemove(itemCd, out _);
            }
            else
            {
                Images[itemCd] = image;
            }
            return Task.FromResult(true);
        }

        private static ItemRow ToRow(
            long id, Item item, DateTimeOffset createdAt, string createdBy, DateTimeOffset updatedAt, string updatedBy)
        {
            var f = item.Fields;
            return new ItemRow(
                id, item.ItemCd, f.ItemName, f.ItemName2, f.ItemDesc, f.ItemType, f.Unit, f.ItemClass, f.ValidFlag, f.UsdPrc, f.SoUsdPrc,
                f.UpcCode, f.Location, f.ReorderPoint, f.Memo1, f.Memo2, f.Memo3, f.Memo4, f.Memo5, f.ItemGrade, f.Taxable ?? true,
                f.VatKind ?? Item.VatTaxable, item.Plating, item.ItemGroupCd, item.MvgCost, item.OnhandQty, item.StkVal,
                createdAt, createdBy, updatedAt, updatedBy);
        }
    }

    public sealed class InMemoryUserProfileRepository : IUserProfileRepository
    {
        private static readonly DateTimeOffset Start = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);

        public ConcurrentDictionary<long, UserProfileRow> Rows { get; } = new()
        {
            [7] = new UserProfileRow(7, UserId, "Kim", null, null, "ko", null, "W01", "Main", Start),
            [ProfileUserKey] = new UserProfileRow(ProfileUserKey, ProfileUserId, "Profile", null, null, "ko", null, "W01", "Main", Start),
        };

        public Task<UserProfileRow?> FindAsync(long appUserId, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(Rows.GetValueOrDefault(appUserId));

        public Task<bool> IsLanguageInUseAsync(string langCode, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(langCode is "ko" or "en");

        public Task<bool> IsWarehouseInUseAsync(long warehouseId, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(warehouseId == 1);

        public Task<bool> UpdateAsync(
            long appUserId, UserProfileFields fields, DateTimeOffset expectedUpdatedAt, string updatedBy,
            IDbTransaction transaction, CancellationToken cancellationToken)
        {
            var row = Rows[appUserId];
            if (row.UpdatedAt != expectedUpdatedAt)
            {
                return Task.FromResult(false);
            }
            Rows[appUserId] = row with
            {
                UserName = fields.UserName,
                Email = fields.Email,
                Phone = fields.Phone,
                LangCode = fields.LangCode,
                DefaultWarehouseId = fields.DefaultWarehouseId,
                UpdatedAt = row.UpdatedAt.AddSeconds(1),
            };
            return Task.FromResult(true);
        }
    }
}
