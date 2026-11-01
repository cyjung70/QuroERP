using Dapper;
using FluentAssertions;
using QuroErp.Application.Bc.Tax;
using QuroErp.Application.Common.I18n;
using QuroErp.Application.Common.Persistence;
using QuroErp.Domain.Bc.Tax;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Bc.Tax;

/// <summary>Tax code management against PostgreSQL (BC-UC-028, 0130, NONE from 0400). Repository tests roll back by not committing.</summary>
[Collection(AuthSchemaDefinition.Name)]
public class TaxCodeRepositoryTests(AuthSchemaFixture db)
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static readonly TaxCodeFields Vat10 = new(
        TaxKind.VatStandard, 10m, null, true, true, new Dictionary<string, string?> { ["en"] = "VAT 10%", ["ko"] = "과세 10%" });

    [DatabaseFact, Trait("Id", "BC-UC-028-IT-01")]
    public async Task InsertUpdateNamesAndDelete_RoundTrip()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<ITaxCodeRepository>();

        var id = await repository.InsertAsync("ZVAT10", Vat10, "kim", uow.Transaction, None);
        await repository.SaveNamesAsync(id, Vat10.Names, "kim", uow.Transaction, None);
        var inserted = (await repository.FindAllAsync(forUpdate: true, uow.Transaction, None)).Single(t => t.Id == id);
        var stale = await repository.UpdateAsync(id, "ZVAT10", Vat10, inserted.UpdatedAt.AddSeconds(-1), "lee", uow.Transaction, None);
        var updated = await repository.UpdateAsync(id, "ZVAT12", Vat10 with { Rate = 12.5m, UseYn = false }, inserted.UpdatedAt, "lee", uow.Transaction, None);
        await repository.SaveNamesAsync(id, new Dictionary<string, string?> { ["en"] = "VAT 12.5%", ["ko"] = null }, "lee", uow.Transaction, None);
        var names = await db.Get<ITranslationRepository>().FindAsync(TranslationTarget.TaxCodeName, [id], uow.Transaction, None);
        var after = (await repository.FindAllAsync(forUpdate: false, uow.Transaction, None)).Single(t => t.Id == id);
        await repository.DeleteAsync([id], uow.Transaction, None);
        var translationsLeft = await uow.Transaction.Connection!.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM bc_tax_code_translation WHERE tax_code_id = @Id", new { Id = id }, uow.Transaction);

        inserted.Should().BeEquivalentTo(new { TaxCode = "ZVAT10", TaxKind = TaxKind.VatStandard, Rate = 10m, PriceIncluded = true, UseYn = true });
        stale.Should().BeFalse();
        updated.Should().BeTrue();
        after.Should().BeEquivalentTo(new { TaxCode = "ZVAT12", Rate = 12.5m, UseYn = false });
        names.Select(n => (n.LangCode, n.Name)).Should().Equal(("en", "VAT 12.5%"));
        translationsLeft.Should().Be(0);
        (await repository.FindAllAsync(forUpdate: false, uow.Transaction, None)).Should().NotContain(t => t.Id == id);
    }

    [DatabaseFact, Trait("Id", "BC-UC-028-IT-02"), Trait("Rule", "BC-BR-503")]
    public async Task SalesTax_KeepsItsArea_AndTheListShowsTheAreaCode_InCodeOrder()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var areaId = await uow.Transaction.Connection!.ExecuteScalarAsync<long>(
            """
            INSERT INTO bc_tax_area (tax_area_code, tax_area_name, state_code, created_by, updated_by)
            VALUES ('ZNY', 'New York City', 'NY', 't', 't') RETURNING id
            """, transaction: uow.Transaction);
        var repository = db.Get<ITaxCodeRepository>();

        var id = await repository.InsertAsync(
            "ZNYC", new TaxCodeFields(TaxKind.SalesTax, 0, areaId, false, true, Vat10.Names), "kim", uow.Transaction, None);
        var all = await repository.FindAllAsync(forUpdate: false, uow.Transaction, None);

        all.Select(t => t.TaxCode).Should().BeInAscendingOrder(StringComparer.Ordinal).And.Contain("NONE");
        all.Single(t => t.Id == id).Should().BeEquivalentTo(new { TaxAreaId = areaId, TaxAreaCode = "ZNY" });
        (await repository.FindTaxAreasAsync([areaId, -1], uow.Transaction, None)).Should().BeEquivalentTo([areaId]);
    }

    [DatabaseFact, Trait("Id", "BC-UT-506"), Trait("Rule", "BC-BR-506")]
    public async Task FindUsed_ReadsDocumentLinesForDocuments_AndCustomersAndVendorsForPartners()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<ITaxCodeRepository>();
        var onLine = await repository.InsertAsync("ZLINE", Vat10, "t", uow.Transaction, None);
        var onCustomer = await repository.InsertAsync("ZCUST", Vat10, "t", uow.Transaction, None);
        var onVendor = await repository.InsertAsync("ZVEND", Vat10, "t", uow.Transaction, None);
        var unused = await repository.InsertAsync("ZFREE", Vat10, "t", uow.Transaction, None);
        await uow.Transaction.Connection!.ExecuteAsync(
            """
            WITH po AS (
                INSERT INTO mm_purchase_order (po_no, currency_code, exchange_rate, business_place_id, created_by, updated_by)
                VALUES ('ZZPO506', 'USD', 1, (SELECT id FROM bc_business_place WHERE is_default), 't', 't') RETURNING id)
            INSERT INTO mm_purchase_order_line (purchase_order_id, seq, tax_code_id, created_by, updated_by)
            SELECT id, 1, @OnLine, 't', 't' FROM po;
            INSERT INTO bc_customer (c_name, tax_code_id, created_by, updated_by) VALUES ('Customer 506', @OnCustomer, 't', 't');
            INSERT INTO bc_vendor (v_name, tax_code_id, created_by, updated_by) VALUES ('Vendor 506', @OnVendor, 't', 't');
            """,
            new { OnLine = onLine, OnCustomer = onCustomer, OnVendor = onVendor },
            uow.Transaction);
        long[] ids = [onLine, onCustomer, onVendor, unused];

        var byDocuments = await repository.FindUsedByDocumentsAsync(ids, uow.Transaction, None);
        var byPartners = await repository.FindUsedByPartnersAsync(ids, uow.Transaction, None);

        byDocuments.Should().BeEquivalentTo([onLine]);
        byPartners.Should().BeEquivalentTo([onCustomer, onVendor]);
        (await repository.FindUsedByDocumentsAsync([], uow.Transaction, None)).Should().BeEmpty();
    }
}
