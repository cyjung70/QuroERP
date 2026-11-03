using System.Data;
using Dapper;
using QuroErp.Application.Bc.Tax;

namespace QuroErp.Infrastructure.Bc.Tax;

internal sealed class TaxRepository : ITaxRepository
{
    private const string SelectTaxCodes = """
        SELECT id, tax_code, tax_kind, rate, tax_area_id, price_included, use_yn
        FROM bc_tax_code
        WHERE id = ANY(@Ids)
        ORDER BY id
        """;

    private const string SelectTaxAreaRates = """
        SELECT effective_from, rate FROM bc_tax_area_rate WHERE tax_area_id = @TaxAreaId ORDER BY effective_from
        """;

    private const string SelectExemptions = """
        SELECT certificate_no, state_code, valid_to
        FROM sd_customer_tax_exemption
        WHERE customer_id = @CustomerId AND state_code = @StateCode
        ORDER BY certificate_no
        """;

    public async Task<IReadOnlyList<TaxCodeRow>> FindTaxCodesAsync(
        IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<TaxCodeRow>(new CommandDefinition(
            SelectTaxCodes, new { Ids = ids.ToArray() }, transaction, cancellationToken: cancellationToken))).AsList();

    public async Task<IReadOnlyList<TaxAreaRateRow>> FindTaxAreaRatesAsync(
        long taxAreaId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<TaxAreaRateRow>(new CommandDefinition(
            SelectTaxAreaRates, new { TaxAreaId = taxAreaId }, transaction, cancellationToken: cancellationToken))).AsList();

    public async Task<IReadOnlyList<TaxExemptionRow>> FindExemptionsAsync(
        long customerId, string stateCode, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<TaxExemptionRow>(new CommandDefinition(
            SelectExemptions, new { CustomerId = customerId, StateCode = stateCode }, transaction,
            cancellationToken: cancellationToken))).AsList();
}
