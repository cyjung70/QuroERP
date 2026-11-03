using System.Data;
using Dapper;
using QuroErp.Application.Common.I18n;

namespace QuroErp.Infrastructure.Common.I18n;

internal sealed class TranslationRepository : ITranslationRepository
{
    private const string SelectCodeMajor = """
        SELECT major_id AS owner_id, lang_code, name FROM sys_code_major_translation WHERE major_id = ANY(@OwnerIds)
        """;

    private const string SelectCodeMinor = """
        SELECT minor_id AS owner_id, lang_code, name FROM sys_code_minor_translation WHERE minor_id = ANY(@OwnerIds)
        """;

    private const string SelectItemName = """
        SELECT item_id AS owner_id, lang_code, item_name AS name FROM bc_item_translation WHERE item_id = ANY(@OwnerIds)
        """;

    private const string SelectCurrencyName = """
        SELECT currency_id AS owner_id, lang_code, name FROM bc_currency_translation WHERE currency_id = ANY(@OwnerIds)
        """;

    private const string SelectTaxCodeName = """
        SELECT tax_code_id AS owner_id, lang_code, name FROM bc_tax_code_translation WHERE tax_code_id = ANY(@OwnerIds)
        """;

    public async Task<IReadOnlyList<TranslationRow>> FindAsync(
        TranslationTarget target, IReadOnlyCollection<long> ownerIds, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var sql = target switch
        {
            TranslationTarget.CodeMajor => SelectCodeMajor,
            TranslationTarget.CodeMinor => SelectCodeMinor,
            TranslationTarget.ItemName => SelectItemName,
            TranslationTarget.CurrencyName => SelectCurrencyName,
            TranslationTarget.TaxCodeName => SelectTaxCodeName,
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
        };
        var rows = await transaction.Connection!.QueryAsync<TranslationRow>(
            new CommandDefinition(sql, new { OwnerIds = ownerIds.ToArray() }, transaction, cancellationToken: cancellationToken));
        return rows.AsList();
    }
}
