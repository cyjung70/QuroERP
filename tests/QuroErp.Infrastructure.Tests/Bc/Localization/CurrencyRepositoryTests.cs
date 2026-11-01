using Dapper;
using FluentAssertions;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Common.I18n;
using QuroErp.Application.Common.Persistence;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Bc.Localization;

/// <summary>Currency management against PostgreSQL (BC-UC-026, seed 0102). Repository tests roll back by not committing.</summary>
[Collection(AuthSchemaDefinition.Name)]
public class CurrencyRepositoryTests(AuthSchemaFixture db)
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static readonly string[] Seeded = ["USD", "KRW"];

    private static readonly CurrencyFields Yen = new(0, "JP", true, new Dictionary<string, string?> { ["en"] = "Japanese Yen", ["ko"] = "엔" });

    [DatabaseFact, Trait("Id", "BC-UC-026-IT-01")]
    public async Task InsertUpdateNamesAndDelete_RoundTrip()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<ICurrencyRepository>();

        var id = await repository.InsertAsync("JPY", Yen, "kim", uow.Transaction, None);
        await repository.SaveNamesAsync(id, Yen.Names, "kim", uow.Transaction, None);
        var inserted = (await repository.FindAllAsync(forUpdate: true, uow.Transaction, None)).Single(c => c.Id == id);
        var stale = await repository.UpdateAsync(id, Yen with { Decimals = 1 }, inserted.UpdatedAt.AddSeconds(-1), "lee", uow.Transaction, None);
        var updated = await repository.UpdateAsync(id, Yen with { CountryCode = null, UseYn = false }, inserted.UpdatedAt, "lee", uow.Transaction, None);
        await repository.SaveNamesAsync(id, new Dictionary<string, string?> { ["en"] = "Yen", ["ko"] = null }, "lee", uow.Transaction, None);
        var names = await db.Get<ITranslationRepository>().FindAsync(TranslationTarget.CurrencyName, [id], uow.Transaction, None);
        var after = (await repository.FindAllAsync(forUpdate: false, uow.Transaction, None)).Single(c => c.Id == id);
        await repository.DeleteAsync([id], uow.Transaction, None);
        var translationsLeft = await uow.Transaction.Connection!.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM bc_currency_translation WHERE currency_id = @Id", new { Id = id }, uow.Transaction);

        inserted.Should().BeEquivalentTo(new { CurrencyCode = "JPY", Decimals = 0, CountryCode = "JP", UseYn = true });
        stale.Should().BeFalse();
        updated.Should().BeTrue();
        after.Should().BeEquivalentTo(new { Decimals = 0, CountryCode = (string?)null, UseYn = false });
        names.Select(n => (n.LangCode, n.Name)).Should().Equal(("en", "Yen"));
        translationsLeft.Should().Be(0);
        (await repository.FindAllAsync(forUpdate: false, uow.Transaction, None)).Should().NotContain(c => c.Id == id);
    }

    [DatabaseFact, Trait("Id", "BC-UC-026-IT-02")]
    public async Task FindAll_ReturnsTheSeededCurrenciesInCodeOrder_WithTheirCountries()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);

        var all = await db.Get<ICurrencyRepository>().FindAllAsync(forUpdate: false, uow.Transaction, None);

        all.Select(c => c.CurrencyCode).Should().BeInAscendingOrder(StringComparer.Ordinal);
        all.Should().ContainEquivalentOf(new { CurrencyCode = "USD", Decimals = 2, CountryCode = "US" });
        all.Should().ContainEquivalentOf(new { CurrencyCode = "KRW", Decimals = 0, CountryCode = "KR" });
    }

    [DatabaseFact, Trait("Id", "BC-UC-026-IT-03"), Trait("Rule", "BC-BR-411")]
    public async Task FindUsedByDocuments_ReturnsTheCodesAShipmentStores()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var stored = (await uow.Transaction.Connection!.QueryAsync<string>(
            "SELECT DISTINCT currency_code FROM sd_shipment", transaction: uow.Transaction)).ToHashSet();

        var used = await db.Get<ICurrencyRepository>().FindUsedByDocumentsAsync(["USD", "KRW", "ZZZ"], uow.Transaction, None);

        used.Should().BeEquivalentTo(Seeded.Where(stored.Contains));
        (await db.Get<ICurrencyRepository>().FindUsedByDocumentsAsync([], uow.Transaction, None)).Should().BeEmpty();
    }

    [DatabaseFact, Trait("Id", "BC-UT-416"), Trait("Rule", "BC-BR-416")]
    public async Task FindUsed_ReadsPurchaseOrdersForDocuments_AndCustomersAndVendorsForPartners()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        await uow.Transaction.Connection!.ExecuteAsync(
            """
            INSERT INTO mm_purchase_order (po_no, currency_code, exchange_rate, business_place_id, created_by, updated_by)
            VALUES ('ZZPO416', 'ZZD', 1, (SELECT id FROM bc_business_place WHERE is_default), 't', 't');
            INSERT INTO bc_customer (c_name, currency_code, created_by, updated_by) VALUES ('Customer 416', 'ZZC', 't', 't');
            INSERT INTO bc_vendor (v_name, currency_code, created_by, updated_by) VALUES ('Vendor 416', 'ZZV', 't', 't');
            """,
            transaction: uow.Transaction);
        var repository = db.Get<ICurrencyRepository>();
        string[] codes = ["ZZD", "ZZC", "ZZV", "ZZN"];

        var byDocuments = await repository.FindUsedByDocumentsAsync(codes, uow.Transaction, None);
        var byPartners = await repository.FindUsedByPartnersAsync(codes, uow.Transaction, None);

        byDocuments.Should().BeEquivalentTo(["ZZD"]);
        byPartners.Should().BeEquivalentTo(["ZZC", "ZZV"]);
    }
}
