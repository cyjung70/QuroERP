using System.Collections.Concurrent;
using System.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QuroErp.Application.Bc.Companies;
using QuroErp.Application.Bc.Items;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Bc.UserProfiles;
using QuroErp.Application.Common.Auth;
using QuroErp.Application.Common.Codes;
using QuroErp.Application.Common.I18n;
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Mm.Inventory;
using QuroErp.Application.Mm.Warehouses;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Contracts.Mm.Warehouses;
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

    public InMemoryAutoLoginTokenRepository AutoLoginTokens { get; } = new();

    public ConcurrentQueue<UserLoginLog> LoginLogs { get; } = new();

    public InMemoryPasswordHistoryRepository PasswordHistory { get; } = new();

    /// <summary>Company logo state; there is one company and no logo at start.</summary>
    public InMemoryCompanyRepository Companies { get; } = new();

    /// <summary>Localization state: a KR company (default language ko), languages en/ko (sw off), KRW and USD.</summary>
    public InMemoryLocalizationRepository Localization { get; } = new();

    /// <summary>Applies localization setting writes to <see cref="Localization"/>. Documents exist for none of the modules.</summary>
    public InMemoryLocalizationSettingRepository LocalizationSettings => _localizationSettings ??= new(Localization);

    private InMemoryLocalizationSettingRepository? _localizationSettings;

    /// <summary>Items; empty at start. Ids in <see cref="InMemoryItemRepository.InUse"/> count as used (MM-BR-807).</summary>
    public InMemoryItemRepository Items { get; } = new();

    /// <summary>Warehouse choice list the API returns as is (the repository filters and sorts).</summary>
    public List<WarehouseOptionDto> WarehouseOptions { get; } = [new(1, "MAIN", "Main"), new(2, "W02", "Second")];

    /// <summary>My info rows of kim (7) and profile (16). Languages ko and en and warehouse 1 are in use.</summary>
    public InMemoryUserProfileRepository UserProfiles { get; } = new();

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
            services.AddSingleton<ITranslationRepository, FakeTranslationRepository>();
            services.AddSingleton<ICompanyRepository>(Companies);
            services.AddSingleton<ILocalizationRepository>(Localization);
            services.AddSingleton<ILocalizationSettingRepository>(LocalizationSettings);
            services.AddSingleton<IInventorySettingRepository>(InventorySettings);
            services.AddSingleton<IItemRepository>(Items);
            services.AddSingleton<IWarehouseRepository>(new FakeWarehouseRepository(this));
            services.AddSingleton<IUserProfileRepository>(UserProfiles);
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
                Id = u.Id, UserId = u.UserId, PasswordHash = passwordHash, UserName = u.UserName, RoleCode = u.RoleCode,
                ValidFlag = u.ValidFlag, LangCode = u.LangCode, PasswordChangedAt = now, TokenVersion = u.TokenVersion + 1,
            };
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuthMenu>> FindMenusAsync(long appUserId, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AuthMenu>>([new AuthMenu("BC01", "Company", "bc-company", "BC", null, "Y")]);

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

    /// <summary>VALID_FLAG = Y, N, S (S has no translation). Item codes: ITEM_TYPE, UNIT, ITEM_CLASS, GRADE.</summary>
    private sealed class FakeCodeRepository : ICodeRepository
    {
        private static readonly List<CodeMinorRow> ValidFlag = [new(1, "Y", 10), new(2, "N", 20), new(3, "S", 30)];

        private static readonly Dictionary<string, string[]> ItemCodes = new()
        {
            ["ITEM_TYPE"] = ["INVENTORY", "SET", "SERVICE"], ["UNIT"] = ["EA", "PCS"], ["ITEM_CLASS"] = ["PART"], ["GRADE"] = ["A01"],
        };

        public Task<IReadOnlyList<CodeMinorRow>?> FindActiveMinorsAsync(
            string majorCode, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CodeMinorRow>?>(majorCode == "VALID_FLAG" ? ValidFlag : null);

        public Task<bool> ExistsActiveAsync(string majorCode, string minorCode, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(majorCode == "VALID_FLAG"
                ? ValidFlag.Any(m => m.MinorCode == minorCode)
                : ItemCodes.TryGetValue(majorCode, out var minors) && minors.Contains(minorCode));
    }

    private sealed class FakeTranslationRepository : ITranslationRepository
    {
        private static readonly List<TranslationRow> Rows =
        [
            new(1, "ko", "사용"), new(1, "en", "Used"),
            new(2, "ko", "사용 안 함"), new(2, "en", "Not Used"),
            new(101, "ko", "대한민국 원"), new(101, "en", "Korean Won"),
            new(102, "en", "US Dollar"),
        ];

        public Task<IReadOnlyList<TranslationRow>> FindAsync(
            TranslationTarget target, IReadOnlyCollection<long> ownerIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TranslationRow>>(Rows.Where(r => ownerIds.Contains(r.OwnerId)).ToList());
    }

    private sealed class FakeApiClientRepository(Dictionary<string, ApiClient> clients) : IApiClientRepository
    {
        public Task<ApiClient?> FindByKeyHashAsync(string keyHash, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(clients.GetValueOrDefault(keyHash));
    }

    public sealed class InMemoryCompanyRepository : ICompanyRepository
    {
        public CompanyLogoRow? Logo { get; set; }

        public string? UpdatedBy { get; private set; }

        public Task<CompanyLogoRow?> FindLogoAsync(IDbTransaction transaction, CancellationToken cancellationToken) => Task.FromResult(Logo);

        public Task<bool> UpdateLogoAsync(
            byte[] image, string contentType, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Logo = new CompanyLogoRow(image, contentType);
            UpdatedBy = updatedBy;
            return Task.FromResult(true);
        }
    }

    public sealed class InMemoryLocalizationRepository : ILocalizationRepository
    {
        public CompanyLocalizationRow? Company { get; set; } = new("KR", "KRW", "ko", "DOCUMENT");

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
            _rows[id] = ToRow(id, item, new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero));
            return Task.FromResult(id);
        }

        public Task<bool> UpdateAsync(
            Item item, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            if (!_rows.TryGetValue(item.Id, out var row) || row.UpdatedAt != expectedUpdatedAt)
            {
                return Task.FromResult(false);
            }
            _rows[item.Id] = ToRow(item.Id, item, row.UpdatedAt.AddSeconds(1));
            return Task.FromResult(true);
        }

        public Task<bool> IsTypeLockedAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(TypeLocked.Contains(id));

        public Task<bool> IsInUseAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(InUse.Contains(id));

        public Task<bool> DeleteAsync(long id, DateTimeOffset expectedUpdatedAt, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(_rows.TryGetValue(id, out var row) && row.UpdatedAt == expectedUpdatedAt && _rows.TryRemove(id, out _));

        private static ItemRow ToRow(long id, Item item, DateTimeOffset updatedAt)
        {
            var f = item.Fields;
            return new ItemRow(
                id, item.ItemCd, f.ItemName, f.ItemName2, f.ItemDesc, f.ItemType, f.Unit, f.ItemClass, f.ValidFlag, f.UsdPrc, f.SoUsdPrc,
                f.UpcCode, f.Location, f.ReorderPoint, f.Memo1, f.Memo2, f.Memo3, f.Memo4, f.Memo5, f.ItemGrade, f.Taxable ?? true,
                f.VatKind ?? Item.VatTaxable, item.Plating, item.ItemGroupCd, item.MvgCost, item.OnhandQty, item.StkVal, updatedAt);
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
                UserName = fields.UserName, Email = fields.Email, Phone = fields.Phone, LangCode = fields.LangCode,
                DefaultWarehouseId = fields.DefaultWarehouseId, UpdatedAt = row.UpdatedAt.AddSeconds(1),
            };
            return Task.FromResult(true);
        }
    }
}
