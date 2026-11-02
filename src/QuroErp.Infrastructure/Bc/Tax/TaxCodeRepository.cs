using System.Data;
using System.Text.RegularExpressions;
using Dapper;
using QuroErp.Application.Bc.Tax;
using QuroErp.Domain.Bc.Tax;

namespace QuroErp.Infrastructure.Bc.Tax;

internal sealed partial class TaxCodeRepository : ITaxCodeRepository
{
    private const string SelectAll = """
        SELECT t.id, t.tax_code, t.tax_kind, t.rate, t.tax_area_id, a.tax_area_code, t.price_included, t.use_yn, t.updated_at
        FROM bc_tax_code t
        LEFT JOIN bc_tax_area a ON a.id = t.tax_area_id
        ORDER BY t.tax_code
        """;

    private const string LockAll = SelectAll + " FOR UPDATE OF t";

    private const string SelectTaxAreas = "SELECT id FROM bc_tax_area WHERE id = ANY(@Ids)";

    private const string Insert = """
        INSERT INTO bc_tax_code (tax_code, tax_kind, rate, tax_area_id, price_included, use_yn, created_by, updated_by)
        VALUES (@TaxCode, @TaxKind, @Rate, @TaxAreaId, @PriceIncluded, @UseYn, @By, @By)
        RETURNING id
        """;

    private const string Update = """
        UPDATE bc_tax_code
        SET tax_code = @TaxCode, tax_kind = @TaxKind, rate = @Rate, tax_area_id = @TaxAreaId, price_included = @PriceIncluded,
            use_yn = @UseYn, updated_at = now(), updated_by = @By
        WHERE id = @Id AND updated_at = @ExpectedUpdatedAt
        """;

    private const string DeleteNames = "DELETE FROM bc_tax_code_translation WHERE tax_code_id = @TaxCodeId AND lang_code = ANY(@LangCodes)";

    private const string UpsertName = """
        INSERT INTO bc_tax_code_translation (tax_code_id, lang_code, name, created_by, updated_by)
        VALUES (@TaxCodeId, @LangCode, @Name, @By, @By)
        ON CONFLICT (tax_code_id, lang_code) DO UPDATE
        SET name = EXCLUDED.name, updated_at = now(), updated_by = EXCLUDED.updated_by
        WHERE bc_tax_code_translation.name <> EXCLUDED.name
        """;

    private const string Delete = "DELETE FROM bc_tax_code WHERE id = ANY(@Ids)";

    public async Task<IReadOnlyList<TaxCodeRecord>> FindAllAsync(bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<(
            long Id, string TaxCode, string TaxKind, decimal Rate, long? TaxAreaId, string? TaxAreaCode, bool PriceIncluded, bool UseYn,
            DateTime UpdatedAt)>(new CommandDefinition(forUpdate ? LockAll : SelectAll, transaction: transaction, cancellationToken: cancellationToken)))
        .Select(r => new TaxCodeRecord(
            r.Id, r.TaxCode, r.TaxKind, r.Rate, r.TaxAreaId, r.TaxAreaCode, r.PriceIncluded, r.UseYn,
            new DateTimeOffset(DateTime.SpecifyKind(r.UpdatedAt, DateTimeKind.Utc))))
        .ToList();

    public Task<IReadOnlySet<long>> FindUsedByDocumentsAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken) =>
        FindUsedAsync(ids, TaxCodeRules.DocumentColumns, transaction, cancellationToken);

    public Task<IReadOnlySet<long>> FindUsedByPartnersAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken) =>
        FindUsedAsync(ids, TaxCodeRules.PartnerColumns, transaction, cancellationToken);

    public async Task<IReadOnlySet<long>> FindTaxAreasAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken) =>
        ids.Count == 0
            ? new HashSet<long>()
            : (await transaction.Connection!.QueryAsync<long>(new CommandDefinition(
                SelectTaxAreas, new { Ids = ids.ToArray() }, transaction, cancellationToken: cancellationToken))).ToHashSet();

    public Task<long> InsertAsync(string taxCode, TaxCodeFields fields, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<long>(new CommandDefinition(
            Insert,
            new { TaxCode = taxCode, fields.TaxKind, fields.Rate, fields.TaxAreaId, fields.PriceIncluded, fields.UseYn, By = createdBy },
            transaction,
            cancellationToken: cancellationToken));

    public async Task<bool> UpdateAsync(
        long id, string taxCode, TaxCodeFields fields, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction,
        CancellationToken cancellationToken) =>
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Update,
            new
            {
                Id = id,
                TaxCode = taxCode,
                fields.TaxKind,
                fields.Rate,
                fields.TaxAreaId,
                fields.PriceIncluded,
                fields.UseYn,
                By = updatedBy,
                ExpectedUpdatedAt = expectedUpdatedAt.UtcDateTime,
            },
            transaction,
            cancellationToken: cancellationToken)) == 1;

    public async Task SaveNamesAsync(
        long taxCodeId, IReadOnlyDictionary<string, string?> names, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var removed = names.Where(n => n.Value is null).Select(n => n.Key).ToArray();
        if (removed.Length > 0)
        {
            await transaction.Connection!.ExecuteAsync(new CommandDefinition(
                DeleteNames, new { TaxCodeId = taxCodeId, LangCodes = removed }, transaction, cancellationToken: cancellationToken));
        }
        foreach (var (langCode, name) in names.Where(n => n.Value is not null))
        {
            await transaction.Connection!.ExecuteAsync(new CommandDefinition(
                UpsertName, new { TaxCodeId = taxCodeId, LangCode = langCode, Name = name, By = updatedBy }, transaction, cancellationToken: cancellationToken));
        }
    }

    public Task DeleteAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken) =>
        ids.Count == 0
            ? Task.CompletedTask
            : transaction.Connection!.ExecuteAsync(new CommandDefinition(Delete, new { Ids = ids.ToArray() }, transaction, cancellationToken: cancellationToken));

    private static async Task<IReadOnlySet<long>> FindUsedAsync(
        IReadOnlyCollection<long> ids, IReadOnlyList<TaxCodeColumn> columns, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return new HashSet<long>();
        }
        return (await transaction.Connection!.QueryAsync<long>(new CommandDefinition(
            UsedSql(columns), new { Ids = ids.ToArray() }, transaction, cancellationToken: cancellationToken)))
            .ToHashSet();
    }

    /// <summary>
    /// One SELECT per column. Table and column names come only from the Domain list, never from a request, and are checked
    /// against a plain identifier pattern before they go into the SQL; the ids are a parameter.
    /// </summary>
    private static string UsedSql(IEnumerable<TaxCodeColumn> columns) =>
        string.Join("\nUNION\n", columns.Select(c =>
            $"SELECT \"{Identifier(c.Column)}\" FROM \"{Identifier(c.Table)}\" WHERE \"{Identifier(c.Column)}\" = ANY(@Ids)"));

    private static string Identifier(string name) =>
        IdentifierPattern().IsMatch(name) ? name : throw new InvalidOperationException($"Not a plain identifier: {name}");

    [GeneratedRegex("^[a-z_][a-z0-9_]*$")]
    private static partial Regex IdentifierPattern();
}
