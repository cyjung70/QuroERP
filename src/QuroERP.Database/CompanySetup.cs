using Npgsql;

namespace QuroERP.Database;

/// <summary>
/// Sets the company country at installation (BC-BR-313): country, base currency, default language, time zone
/// (BC-BR-318) and the module quantity decimals and rounding mode all come from the country profile
/// (US = USD/en/America/New_York, KR = KRW/ko/Asia/Seoul). The time zone can be given instead of the default.
/// Unit price decimals (bc_country_decimal) are rows for every country and do not change with the company country.
/// Blocked once any document, stock movement or exchange rate exists. A new company also names the default business place.
/// </summary>
public static class CompanySetup
{
    public const string AuditUser = "system";

    /// <summary>Country defaults. The Database tool references no other project (layers.md), so these copy
    /// ICountryProfile (UsCountryProfile, KrCountryProfile); a test keeps them equal.</summary>
    public static IReadOnlyDictionary<string, CountryDefaults> Countries { get; } = new Dictionary<string, CountryDefaults>
    {
        ["US"] = new("US", "USD", "en", "America/New_York", [new("MM", 2, "HALF_UP"), new("SD", 0, "HALF_UP")]),
        ["KR"] = new("KR", "KRW", "ko", "Asia/Seoul", [new("MM", 2, "HALF_UP"), new("SD", 0, "HALF_UP")]),
    };

    // Tables whose rows mean the company has started trading (BC-BR-312, BC-BR-313)
    private const string HasTransactions = """
        SELECT EXISTS (SELECT 1 FROM mm_purchase_order)
            OR EXISTS (SELECT 1 FROM mm_goods_movement)
            OR EXISTS (SELECT 1 FROM mm_ap_payment)
            OR EXISTS (SELECT 1 FROM sd_sales_order)
            OR EXISTS (SELECT 1 FROM sd_sales_invoice)
            OR EXISTS (SELECT 1 FROM sd_ar_receipt)
            OR EXISTS (SELECT 1 FROM bc_exchange_rate)
        """;

    private const string LockCompany = """
        SELECT id FROM bc_company ORDER BY id LIMIT 1 FOR UPDATE
        """;

    private const string MissingMasterValue = """
        SELECT NOT EXISTS (SELECT 1 FROM bc_currency WHERE currency_code = @currency_code AND use_yn)
            OR NOT EXISTS (SELECT 1 FROM sys_language WHERE lang_code = @lang_code AND use_yn)
        """;

    private const string InsertCompany = """
        INSERT INTO bc_company (name, country_code, base_currency_code, default_lang_code, time_zone, created_by, updated_by)
        VALUES (@name, @country_code, @currency_code, @lang_code, @time_zone, @audit_user, @audit_user)
        """;

    private const string UpdateCompany = """
        UPDATE bc_company
        SET name = coalesce(@name, name), country_code = @country_code, base_currency_code = @currency_code,
            default_lang_code = @lang_code, time_zone = @time_zone, updated_at = now(), updated_by = @audit_user
        WHERE id = @id
        """;

    // A new installation has the default business place from the schema script with its code as the name (BC-BR-319)
    private const string NameDefaultBusinessPlace = """
        UPDATE bc_business_place
        SET business_place_name = @name, updated_at = now(), updated_by = @audit_user
        WHERE is_default
        """;

    private const string UpsertDecimalSetting = """
        INSERT INTO sys_decimal_setting (module, qty_decimals, rounding_mode, created_by, updated_by)
        VALUES (@module, @qty_decimals, @rounding_mode, @audit_user, @audit_user)
        ON CONFLICT (module) DO UPDATE
           SET qty_decimals = EXCLUDED.qty_decimals,
               rounding_mode = EXCLUDED.rounding_mode,
               updated_at = now(),
               updated_by = EXCLUDED.updated_by
        """;

    /// <param name="companyName">Required when there is no company yet; otherwise the name changes only when given.</param>
    /// <param name="timeZone">IANA time zone instead of the country default (BC-BR-318).</param>
    /// <returns>true when the company was created, false when the existing company was updated.</returns>
    /// <exception cref="ArgumentException">A country other than US or KR (BC-E-313), or an unknown time zone (BC-E-318).</exception>
    /// <exception cref="InvalidOperationException">Transactions exist, the name is missing, or the currency or language is not in use.</exception>
    public static async Task<bool> SetCountryAsync(
        string connectionString, string countryCode, string? companyName, string? timeZone, CancellationToken cancellationToken)
    {
        var profile = Countries.GetValueOrDefault(countryCode)
            ?? throw new ArgumentException($"BC-E-313: {countryCode}", nameof(countryCode));
        var zone = string.IsNullOrWhiteSpace(timeZone) ? profile.DefaultTimeZone : timeZone.Trim();
        if (!IsKnownTimeZone(zone))
        {
            throw new ArgumentException($"BC-E-318: {zone}", nameof(timeZone));
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var companyId = await ScalarAsync(connection, transaction, LockCompany, cancellationToken) as long?;
        if (await ScalarAsync(connection, transaction, HasTransactions, cancellationToken) is true)
        {
            throw new InvalidOperationException("거래(문서·재고 이동·환율)가 있어 국가를 바꿀 수 없습니다 (BC-BR-313).");
        }
        if (companyId is null && string.IsNullOrWhiteSpace(companyName))
        {
            throw new InvalidOperationException("회사가 없습니다. --company-name으로 회사 이름을 함께 입력하세요.");
        }
        if (await ScalarAsync(connection, transaction, MissingMasterValue, cancellationToken,
                ("currency_code", profile.BaseCurrencyCode), ("lang_code", profile.DefaultLangCode)) is true)
        {
            throw new InvalidOperationException(
                $"통화 {profile.BaseCurrencyCode} 또는 언어 {profile.DefaultLangCode}가 사용 중이 아닙니다. 먼저 마이그레이션(Schema)을 적용하세요.");
        }

        var name = string.IsNullOrWhiteSpace(companyName) ? null : companyName.Trim();
        (string, object?)[] company =
        [
            ("name", name), ("country_code", profile.CountryCode), ("currency_code", profile.BaseCurrencyCode),
            ("lang_code", profile.DefaultLangCode), ("time_zone", zone), ("audit_user", AuditUser), ("id", companyId),
        ];
        await ExecuteAsync(connection, transaction, companyId is null ? InsertCompany : UpdateCompany, cancellationToken, company);
        if (companyId is null)
        {
            await ExecuteAsync(connection, transaction, NameDefaultBusinessPlace, cancellationToken, ("name", name), ("audit_user", AuditUser));
        }

        foreach (var setting in profile.DecimalSettings)
        {
            await ExecuteAsync(connection, transaction, UpsertDecimalSetting, cancellationToken,
                ("module", setting.Module), ("qty_decimals", setting.QtyDecimals),
                ("rounding_mode", setting.RoundingMode), ("audit_user", AuditUser));
        }

        await transaction.CommitAsync(cancellationToken);
        return companyId is null;
    }

    /// <summary>Same check as CompanyRules.IsKnownTimeZone: an IANA name the system knows, not a Windows name.</summary>
    public static bool IsKnownTimeZone(string timeZone) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var zone) && zone.HasIanaId;

    private static async Task<object?> ScalarAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = Command(connection, transaction, sql, parameters);
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = Command(connection, transaction, sql, parameters);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static NpgsqlCommand Command(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, (string Name, object? Value)[] parameters)
    {
        var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters)
        {
            // Typed parameters: @name may be NULL, which Npgsql cannot infer a type for
            command.Parameters.Add(new NpgsqlParameter(name, value ?? DBNull.Value)
            {
                NpgsqlDbType = value is int ? NpgsqlTypes.NpgsqlDbType.Integer
                    : name == "id" ? NpgsqlTypes.NpgsqlDbType.Bigint
                    : NpgsqlTypes.NpgsqlDbType.Varchar,
            });
        }
        return command;
    }
}

/// <summary>Installation defaults of one country (bc_company and sys_decimal_setting values).</summary>
public sealed record CountryDefaults(
    string CountryCode, string BaseCurrencyCode, string DefaultLangCode, string DefaultTimeZone, IReadOnlyList<DecimalDefaults> DecimalSettings);

/// <summary>One sys_decimal_setting row.</summary>
public sealed record DecimalDefaults(string Module, int QtyDecimals, string RoundingMode);
