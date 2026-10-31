using Dapper;
using FluentAssertions;
using QuroErp.Application.Bc.Tax;
using QuroErp.Application.Common.Persistence;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Bc.Tax;

/// <summary>Tax tables (0130) and repository, real PostgreSQL. Each test rolls back.</summary>
[Collection(AuthSchemaDefinition.Name)]
public class TaxRepositoryTests(AuthSchemaFixture db)
{
    [DatabaseFact, Trait("Id", "CORE-015-IT-01")]
    public async Task Reads_TaxCodes_AreaRates_AndExemptionsOfOneState()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);
        var repository = db.Get<ITaxRepository>();
        var ids = await uow.Transaction.Connection!.QuerySingleAsync<(long AreaId, long SalesTaxId, long VatId, long CustomerId)>(
            """
            WITH a AS (INSERT INTO bc_tax_area (tax_area_code, tax_area_name, state_code, created_by, updated_by)
                       VALUES ('ZZ-LA', 'Los Angeles', 'CA', 't', 't') RETURNING id),
                 r AS (INSERT INTO bc_tax_area_rate (tax_area_id, effective_from, rate, created_by, updated_by)
                       SELECT id, d, rt, 't', 't' FROM a, (VALUES (DATE '2026-07-01', 9.5), (DATE '2026-01-01', 9.25)) v(d, rt)),
                 s AS (INSERT INTO bc_tax_code (tax_code, tax_kind, tax_area_id, created_by, updated_by)
                       SELECT 'ZZLA', 'SALES_TAX', id, 't', 't' FROM a RETURNING id),
                 v AS (INSERT INTO bc_tax_code (tax_code, tax_kind, rate, price_included, use_yn, created_by, updated_by)
                       VALUES ('ZZVAT', 'VAT_STANDARD', 10, true, false, 't', 't') RETURNING id),
                 c AS (INSERT INTO bc_customer (c_name, created_by, updated_by) VALUES ('Customer', 't', 't') RETURNING id),
                 e AS (INSERT INTO sd_customer_tax_exemption (customer_id, certificate_no, state_code, valid_to, created_by, updated_by)
                       SELECT id, n, st, vt, 't', 't' FROM c,
                              (VALUES ('B-2', 'CA', NULL::date), ('A-1', 'CA', DATE '2026-12-31'), ('N-1', 'NY', NULL::date)) x(n, st, vt))
            SELECT (SELECT id FROM a), (SELECT id FROM s), (SELECT id FROM v), (SELECT id FROM c)
            """,
            transaction: uow.Transaction);

        var codes = await repository.FindTaxCodesAsync([ids.VatId, ids.SalesTaxId, -1], uow.Transaction, CancellationToken.None);
        var rates = await repository.FindTaxAreaRatesAsync(ids.AreaId, uow.Transaction, CancellationToken.None);
        var exemptions = await repository.FindExemptionsAsync(ids.CustomerId, "CA", uow.Transaction, CancellationToken.None);

        codes.Should().Equal(
            new TaxCodeRow(ids.SalesTaxId, "ZZLA", "SALES_TAX", 0m, ids.AreaId, false, true),
            new TaxCodeRow(ids.VatId, "ZZVAT", "VAT_STANDARD", 10m, null, true, false));
        rates.Should().Equal(new TaxAreaRateRow(new DateOnly(2026, 1, 1), 9.25m), new TaxAreaRateRow(new DateOnly(2026, 7, 1), 9.5m));
        exemptions.Should().Equal(new TaxExemptionRow("A-1", "CA", new DateOnly(2026, 12, 31)), new TaxExemptionRow("B-2", "CA", null));
    }
}
