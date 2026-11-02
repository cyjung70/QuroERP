using Dapper;
using FluentAssertions;
using QuroErp.Application.Bc.Tax;
using QuroErp.Application.Common.Persistence;
using QuroErp.Domain.Bc.Tax;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Bc.Tax;

/// <summary>US tax areas against PostgreSQL (BC-UC-029, 0130). Repository tests roll back by not committing.</summary>
[Collection(AuthSchemaDefinition.Name)]
public class TaxAreaRepositoryTests(AuthSchemaFixture db)
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly DateOnly Jan1 = new(2026, 1, 1);
    private static readonly DateOnly Jul1 = new(2026, 7, 1);
    private static readonly DateOnly Far = new(2099, 1, 1);

    [DatabaseFact, Trait("Id", "BC-UC-029-IT-01"), Trait("Rule", "BC-BR-512")]
    public async Task InsertUpdateRatesAndDelete_RoundTrip()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<ITaxAreaRepository>();
        var fields = new TaxAreaFields("ZZ57A", "Test City", "NY", [new(Jan1, 8m), new(Jul1, 8.5m)]);

        await repository.LockAsync(uow.Transaction, None);
        var id = await repository.InsertAsync(fields, "kim", uow.Transaction, None);
        await repository.SaveRatesAsync(id, fields, "kim", uow.Transaction, None);
        var inserted = (await repository.FindAsync(id, forUpdate: true, uow.Transaction, None))!;
        var stale = await repository.UpdateAsync(id, fields, inserted.UpdatedAt.AddSeconds(-1), "lee", uow.Transaction, None);
        var changed = fields with { TaxAreaName = "Test Town", Rates = [new(Jul1, 8.625m), new(Far, 9m)] };
        var updated = await repository.UpdateAsync(id, changed, inserted.UpdatedAt, "lee", uow.Transaction, None);
        await repository.SaveRatesAsync(id, changed, "lee", uow.Transaction, None);
        var after = await repository.FindAsync(id, forUpdate: false, uow.Transaction, None);
        var rates = await repository.FindRatesAsync(id, uow.Transaction, None);
        var byCode = await repository.FindIdByCodeAsync("ZZ57A", uow.Transaction, None);
        var deleted = await repository.DeleteAsync(id, after!.UpdatedAt, uow.Transaction, None);
        var ratesLeft = await uow.Transaction.Connection!.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM bc_tax_area_rate WHERE tax_area_id = @Id", new { Id = id }, uow.Transaction);

        inserted.Should().BeEquivalentTo(new { TaxAreaCode = "ZZ57A", TaxAreaName = "Test City", StateCode = "NY" });
        stale.Should().BeFalse();
        updated.Should().BeTrue();
        after.TaxAreaName.Should().Be("Test Town");
        rates.Should().Equal(new TaxAreaRate(Jul1, 8.625m), new TaxAreaRate(Far, 9m));
        byCode.Should().Be(id);
        deleted.Should().BeTrue();
        ratesLeft.Should().Be(0);
        (await repository.FindAsync(id, forUpdate: false, uow.Transaction, None)).Should().BeNull();
    }

    [DatabaseFact, Trait("Id", "BC-UC-029-IT-02")]
    public async Task Search_FiltersByKeywordAndState_AndShowsTheRateValidOnTheDay()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<ITaxAreaRepository>();
        var first = new TaxAreaFields("ZZ57B", "Zeta 100% Town", "NY", [new(Jan1, 8m), new(Far, 9m)]);
        var second = new TaxAreaFields("ZZ57C", "Zeta City", "CA", [new(Far, 7m)]);
        foreach (var fields in new[] { first, second })
        {
            await repository.SaveRatesAsync(await repository.InsertAsync(fields, "t", uow.Transaction, None), fields, "t", uow.Transaction, None);
        }

        var all = await repository.SearchAsync("zz57", null, Jul1, 200, uow.Transaction, None);
        var percent = await repository.SearchAsync("100%", null, Jul1, 200, uow.Transaction, None);
        var ca = await repository.SearchAsync("zeta", "CA", Jul1, 200, uow.Transaction, None);

        all.Select(a => (a.TaxAreaCode, a.EffectiveFrom, a.Rate)).Should().Equal(("ZZ57B", Jan1, 8m), ("ZZ57C", null, null));
        percent.Select(a => a.TaxAreaCode).Should().Equal("ZZ57B");
        ca.Select(a => a.TaxAreaCode).Should().Equal("ZZ57C");
        (await repository.SearchAsync("zz57", null, Jul1, 1, uow.Transaction, None)).Should().HaveCount(1);
    }

    [DatabaseFact, Trait("Id", "BC-UC-029-IT-03"), Trait("Rule", "BC-BR-511")]
    public async Task IsUsedByTaxCodes_IsTrueOnlyForAnAreaATaxCodePointsAt()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<ITaxAreaRepository>();
        var used = await repository.InsertAsync(new TaxAreaFields("ZZ57D", "Used", "NY", []), "t", uow.Transaction, None);
        var free = await repository.InsertAsync(new TaxAreaFields("ZZ57E", "Free", "NY", []), "t", uow.Transaction, None);
        await uow.Transaction.Connection!.ExecuteAsync(
            """
            INSERT INTO bc_tax_code (tax_code, tax_kind, tax_area_id, created_by, updated_by)
            VALUES ('ZZ57D', 'SALES_TAX', @Used, 't', 't')
            """,
            new { Used = used },
            uow.Transaction);

        (await repository.IsUsedByTaxCodesAsync(used, uow.Transaction, None)).Should().BeTrue();
        (await repository.IsUsedByTaxCodesAsync(free, uow.Transaction, None)).Should().BeFalse();
        (await repository.FindIdByCodeAsync("ZZ57X", uow.Transaction, None)).Should().BeNull();
    }
}
