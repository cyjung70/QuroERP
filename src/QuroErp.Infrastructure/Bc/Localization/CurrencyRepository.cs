using System.Data;
using System.Text.RegularExpressions;
using Dapper;
using QuroErp.Application.Bc.Localization;
using QuroErp.Domain.Bc.Localization;

namespace QuroErp.Infrastructure.Bc.Localization;

internal sealed partial class CurrencyRepository : ICurrencyRepository
{
    private const string SelectAll = """
        SELECT id, currency_code, decimals, country_code, use_yn, updated_at
        FROM bc_currency
        ORDER BY currency_code
        """;

    private const string LockAll = SelectAll + " FOR UPDATE";

    private const string Insert = """
        INSERT INTO bc_currency (currency_code, decimals, country_code, use_yn, created_by, updated_by)
        VALUES (@CurrencyCode, @Decimals, @CountryCode, @UseYn, @By, @By)
        RETURNING id
        """;

    private const string Update = """
        UPDATE bc_currency
        SET decimals = @Decimals, country_code = @CountryCode, use_yn = @UseYn, updated_at = now(), updated_by = @By
        WHERE id = @Id AND updated_at = @ExpectedUpdatedAt
        """;

    private const string DeleteNames = "DELETE FROM bc_currency_translation WHERE currency_id = @CurrencyId AND lang_code = ANY(@LangCodes)";

    private const string UpsertName = """
        INSERT INTO bc_currency_translation (currency_id, lang_code, name, created_by, updated_by)
        VALUES (@CurrencyId, @LangCode, @Name, @By, @By)
        ON CONFLICT (currency_id, lang_code) DO UPDATE
        SET name = EXCLUDED.name, updated_at = now(), updated_by = EXCLUDED.updated_by
        WHERE bc_currency_translation.name <> EXCLUDED.name
        """;

    private const string Delete = "DELETE FROM bc_currency WHERE id = ANY(@Ids)";

    public async Task<IReadOnlyList<CurrencyRecord>> FindAllAsync(bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<(long Id, string CurrencyCode, int Decimals, string? CountryCode, bool UseYn, DateTime UpdatedAt)>(
            new CommandDefinition(forUpdate ? LockAll : SelectAll, transaction: transaction, cancellationToken: cancellationToken)))
        .Select(r => new CurrencyRecord(r.Id, r.CurrencyCode, r.Decimals, r.CountryCode, r.UseYn, Utc(r.UpdatedAt)))
        .ToList();

    public Task<IReadOnlySet<string>> FindUsedByDocumentsAsync(
        IReadOnlyCollection<string> currencyCodes, IDbTransaction transaction, CancellationToken cancellationToken) =>
        FindUsedAsync(currencyCodes, CurrencyRules.DocumentColumns, transaction, cancellationToken);

    public Task<IReadOnlySet<string>> FindUsedByPartnersAsync(
        IReadOnlyCollection<string> currencyCodes, IDbTransaction transaction, CancellationToken cancellationToken) =>
        FindUsedAsync(currencyCodes, CurrencyRules.PartnerColumns, transaction, cancellationToken);

    public Task<long> InsertAsync(
        string currencyCode, CurrencyFields fields, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<long>(new CommandDefinition(
            Insert,
            new { CurrencyCode = currencyCode, fields.Decimals, fields.CountryCode, fields.UseYn, By = createdBy },
            transaction,
            cancellationToken: cancellationToken));

    public async Task<bool> UpdateAsync(
        long id, CurrencyFields fields, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Update,
            new { Id = id, fields.Decimals, fields.CountryCode, fields.UseYn, By = updatedBy, ExpectedUpdatedAt = expectedUpdatedAt.UtcDateTime },
            transaction,
            cancellationToken: cancellationToken)) == 1;

    public async Task SaveNamesAsync(
        long currencyId, IReadOnlyDictionary<string, string?> names, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var removed = names.Where(n => n.Value is null).Select(n => n.Key).ToArray();
        if (removed.Length > 0)
        {
            await transaction.Connection!.ExecuteAsync(new CommandDefinition(
                DeleteNames, new { CurrencyId = currencyId, LangCodes = removed }, transaction, cancellationToken: cancellationToken));
        }
        foreach (var (langCode, name) in names.Where(n => n.Value is not null))
        {
            await transaction.Connection!.ExecuteAsync(new CommandDefinition(
                UpsertName, new { CurrencyId = currencyId, LangCode = langCode, Name = name, By = updatedBy }, transaction, cancellationToken: cancellationToken));
        }
    }

    public Task DeleteAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken) =>
        ids.Count == 0
            ? Task.CompletedTask
            : transaction.Connection!.ExecuteAsync(new CommandDefinition(Delete, new { Ids = ids.ToArray() }, transaction, cancellationToken: cancellationToken));

    private static async Task<IReadOnlySet<string>> FindUsedAsync(
        IReadOnlyCollection<string> currencyCodes, IReadOnlyList<CurrencyColumn> columns, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        if (currencyCodes.Count == 0 || columns.Count == 0)
        {
            return new HashSet<string>();
        }
        return (await transaction.Connection!.QueryAsync<string>(new CommandDefinition(
            UsedSql(columns), new { Codes = currencyCodes.ToArray() }, transaction, cancellationToken: cancellationToken)))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// One SELECT per column. Table and column names come only from the Domain list, never from a request, and are checked
    /// against a plain identifier pattern before they go into the SQL; the codes are a parameter.
    /// </summary>
    private static string UsedSql(IEnumerable<CurrencyColumn> columns) =>
        string.Join("\nUNION\n", columns.Select(c =>
            $"SELECT \"{Identifier(c.Column)}\" FROM \"{Identifier(c.Table)}\" WHERE \"{Identifier(c.Column)}\" = ANY(@Codes)"));

    private static string Identifier(string name) =>
        IdentifierPattern().IsMatch(name) ? name : throw new InvalidOperationException($"Not a plain identifier: {name}");

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    [GeneratedRegex("^[a-z_][a-z0-9_]*$")]
    private static partial Regex IdentifierPattern();
}
