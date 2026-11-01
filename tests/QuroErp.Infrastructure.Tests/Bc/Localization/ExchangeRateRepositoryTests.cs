using FluentAssertions;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Common.Persistence;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Bc.Localization;

/// <summary>Exchange rates against PostgreSQL (BC-UC-024, 0403). Repository tests roll back by not committing.</summary>
[Collection(AuthSchemaDefinition.Name)]
public class ExchangeRateRepositoryTests(AuthSchemaFixture db)
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly DateOnly Oct1 = new(2026, 10, 1);
    private static readonly DateOnly Oct2 = new(2026, 10, 2);

    [DatabaseFact, Trait("Id", "BC-UC-024-IT-01"), Trait("Rule", "SD-BR-908")]
    public async Task InsertFindUpdateAndDelete_RoundTrip_NewestFirstThenCurrency()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<IExchangeRateRepository>();
        await repository.LockAsync(uow.Transaction, None);

        var krw1 = await repository.InsertAsync(new(Oct1, "KRW", 0.00075m), "kim", uow.Transaction, None);
        var idr1 = await repository.InsertAsync(new(Oct1, "IDR", 0.0000628931m), "kim", uow.Transaction, None);
        var krw2 = await repository.InsertAsync(new(Oct2, "KRW", 0.0007407407m), "kim", uow.Transaction, None);
        var all = await repository.FindAsync(Oct1, Oct2, null, uow.Transaction, None);
        var krwOnly = await repository.FindAsync(Oct1, Oct2, "KRW", uow.Transaction, None);
        var byKeys = await repository.FindByKeysAsync([(Oct1, "IDR"), (Oct2, "IDR")], uow.Transaction, None);
        var stored = (await repository.FindByIdsAsync([krw1, 999_999], uow.Transaction, None)).Single();
        var stale = await repository.UpdateAsync(krw1, new(Oct1, "KRW", 1m), stored.UpdatedAt.AddSeconds(-1), "lee", uow.Transaction, None);
        var updated = await repository.UpdateAsync(krw1, new(Oct1, "KRW", 0.000751m), stored.UpdatedAt, "lee", uow.Transaction, None);
        await repository.DeleteAsync([idr1], uow.Transaction, None);
        var after = await repository.FindAsync(Oct1, Oct2, null, uow.Transaction, None);

        all.Select(r => (r.Id, r.RateDt, r.CurrencyCode)).Should().Equal((krw2, Oct2, "KRW"), (idr1, Oct1, "IDR"), (krw1, Oct1, "KRW"));
        all.Single(r => r.Id == idr1).Rate.Should().Be(0.0000628931m);
        krwOnly.Select(r => r.Id).Should().Equal(krw2, krw1);
        byKeys.Select(r => r.Id).Should().Equal(idr1);
        stale.Should().BeFalse();
        updated.Should().BeTrue();
        after.Select(r => (r.Id, r.Rate)).Should().Equal((krw2, 0.0007407407m), (krw1, 0.000751m));
    }

    [DatabaseFact, Trait("Id", "BC-UC-024-IT-02"), Trait("Rule", "BC-BR-412")]
    public async Task FindLatest_IsTheRateOfTheLatestDateOnOrBefore()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<IExchangeRateRepository>();
        await repository.InsertAsync(new(Oct1, "KRW", 0.00075m), "kim", uow.Transaction, None);
        await repository.InsertAsync(new(Oct2, "KRW", 0.00074m), "kim", uow.Transaction, None);

        (await repository.FindLatestAsync("KRW", new DateOnly(2026, 10, 20), uow.Transaction, None)).Should().Be(0.00074m);
        (await repository.FindLatestAsync("KRW", Oct1, uow.Transaction, None)).Should().Be(0.00075m);
        (await repository.FindLatestAsync("KRW", new DateOnly(2026, 9, 30), uow.Transaction, None)).Should().BeNull();
        (await repository.FindLatestAsync("IDR", Oct2, uow.Transaction, None)).Should().BeNull();
    }

    [DatabaseFact, Trait("Id", "BC-UC-024-IT-03"), Trait("Rule", "SD-BR-906")]
    public async Task TwoRows_SwapTheirDates_InOneTransaction()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<IExchangeRateRepository>();
        var first = await repository.InsertAsync(new(Oct1, "KRW", 0.00075m), "kim", uow.Transaction, None);
        var second = await repository.InsertAsync(new(Oct2, "KRW", 0.00074m), "kim", uow.Transaction, None);
        var rows = (await repository.FindByIdsAsync([first, second], uow.Transaction, None)).ToDictionary(r => r.Id);

        (await repository.UpdateAsync(first, new(Oct2, "KRW", 0.00075m), rows[first].UpdatedAt, "lee", uow.Transaction, None)).Should().BeTrue();
        (await repository.UpdateAsync(second, new(Oct1, "KRW", 0.00074m), rows[second].UpdatedAt, "lee", uow.Transaction, None)).Should().BeTrue();

        (await repository.FindAsync(Oct1, Oct2, "KRW", uow.Transaction, None)).Select(r => r.Id).Should().Equal(first, second);
    }

    [DatabaseFact, Trait("Id", "BC-UC-026-IT-03"), Trait("Rule", "BC-BR-416")]
    public async Task CurrencyUsedByExchangeRates_IsTheCodesThatHaveARate()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        await db.Get<IExchangeRateRepository>().InsertAsync(new ExchangeRateFields(Oct1, "KRW", 0.00075m), "kim", uow.Transaction, None);

        var used = await db.Get<ICurrencyRepository>().FindUsedByExchangeRatesAsync(["KRW", "USD"], uow.Transaction, None);

        used.Should().BeEquivalentTo(["KRW"]);
    }
}
