using Dapper;
using FluentAssertions;
using Npgsql;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Bc.PurchasePrices;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Bc.PurchasePrices;
using QuroErp.Contracts.Common;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Bc.PurchasePrices;
using QuroErp.Domain.Common;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Bc.PurchasePrices;

/// <summary>Own schema for purchase price tests: the service commits, so its rows must not reach other test classes.</summary>
[CollectionDefinition(Name)]
public sealed class PurchasePriceSchemaDefinition : ICollectionFixture<AuthSchemaFixture>
{
    public const string Name = "PurchasePriceSchema";
}

/// <summary>
/// PurchasePriceService with the Dapper repository on real PostgreSQL (BC-UC-015). Without a company the country is US, so MM unit
/// prices have 4 decimals. Each test makes its own price group and items.
/// </summary>
[Collection(PurchasePriceSchemaDefinition.Name)]
public class PurchasePriceServiceTests(AuthSchemaFixture db)
{
    private static readonly CancellationToken None = CancellationToken.None;

    private PurchasePriceService Service => new(db.Get<IPurchasePriceRepository>(), db.Get<ILocalizationRepository>(), db.Get<IUnitOfWorkFactory>());

    [DatabaseFact, Trait("Id", "BC-UC-015-T01"), Trait("Id", "MM-UT-905")]
    public async Task Batch_AddsUpdatesAndDeletes_AndShowsTheStandardPrice()
    {
        var m = await MastersAsync();
        var first = await Service.SaveAsync(m.Group, Batch(added: [Row(m.A, 10m), Row(m.B, 20m)]), "kim", None);
        var a = first.Single(r => r.ItemCd == m.A);
        var b = first.Single(r => r.ItemCd == m.B);

        var saved = await Service.SaveAsync(
            m.Group,
            Batch(added: [Row(m.C, 30m)], updated: [new(a.Id, m.A, 11.5m, a.UpdatedAt)], deleted: [new(b.Id, b.UpdatedAt)]),
            "lee", None);

        saved.Select(r => (r.ItemCd, r.PurchasePrc)).Should().Equal((m.A, 11.5m), (m.C, 30m));
        saved.Single(r => r.ItemCd == m.A).StdPrc.Should().Be(7.25m);
    }

    [DatabaseFact, Trait("Id", "BC-UC-015-T02"), Trait("Id", "MM-UT-904"), Trait("Id", "MM-UT-903")]
    public async Task Batch_OneBadRow_SavesNothing()
    {
        var m = await MastersAsync();

        var act = () => Service.SaveAsync(m.Group, Batch(added: [Row(m.A, 10m), Row(m.B, 0m)]), "kim", None);

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        (error.Code, error.Args).Should().BeEquivalentTo((PurchasePriceRules.PriceCode, new object[] { m.B }));
        (await Service.SearchAsync(m.Group, None)).Should().BeEmpty();
    }

    [DatabaseFact, Trait("Id", "BC-UC-015-T03")]
    public async Task Search_ShowsOnlyTheGroup_GivenInAnyCase()
    {
        var m = await MastersAsync();
        var other = await MastersAsync();
        await Service.SaveAsync(m.Group, Batch(added: [Row(m.A, 10m)]), "kim", None);
        await Service.SaveAsync(other.Group, Batch(added: [Row(m.B, 20m)]), "kim", None);

        var rows = await Service.SearchAsync(" " + m.Group.ToLowerInvariant(), None);

        rows.Select(r => r.ItemCd).Should().Equal(m.A);
    }

    [DatabaseFact, Trait("Id", "MM-UT-901")]
    public async Task Batch_ItemTwice_IsMmE901_ButDeleteAndAddAgainWorks()
    {
        var m = await MastersAsync();
        var row = (await Service.SaveAsync(m.Group, Batch(added: [Row(m.A, 10m)]), "kim", None)).Single();

        var twice = () => Service.SaveAsync(m.Group, Batch(added: [Row(m.A, 12m)]), "kim", None);
        var error = (await twice.Should().ThrowAsync<DomainException>()).Which;
        (error.Code, error.Args).Should().BeEquivalentTo((PurchasePriceRules.KeyCode, new object[] { m.Group, m.A }));

        var again = await Service.SaveAsync(m.Group, Batch(added: [Row(m.A, 12m)], deleted: [new(row.Id, row.UpdatedAt)]), "kim", None);
        again.Select(r => (r.ItemCd, r.PurchasePrc)).Should().Equal((m.A, 12m));
    }

    [DatabaseFact, Trait("Id", "MM-UT-902")]
    public async Task Batch_ServiceUnusedOrUnknownItem_IsMmE902()
    {
        var m = await MastersAsync();

        foreach (var itemCd in new[] { m.Service, m.Unused, "NO-SUCH-" + m.Group })
        {
            var act = () => Service.SaveAsync(m.Group, Batch(added: [Row(itemCd, 10m)]), "kim", None);
            (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(PurchasePriceRules.ItemCode);
        }
    }

    [DatabaseFact, Trait("Id", "BC-UT-429")]
    public async Task Batch_PriceWithMoreDecimalsThanMm_IsBcE423()
    {
        var m = await MastersAsync();

        var act = () => Service.SaveAsync(m.Group, Batch(added: [Row(m.A, 1.23456m)]), "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(DecimalPolicy.TooManyDecimalsCode);
    }

    [DatabaseFact, Trait("Id", "MM-UT-904")]
    public async Task Batch_StaleRow_Is409_AndSavesNothing()
    {
        var m = await MastersAsync();
        var row = (await Service.SaveAsync(m.Group, Batch(added: [Row(m.A, 10m)]), "kim", None)).Single();
        await Service.SaveAsync(m.Group, Batch(updated: [new(row.Id, m.A, 11m, row.UpdatedAt)]), "lee", None);

        var act = () => Service.SaveAsync(
            m.Group, Batch(added: [Row(m.B, 5m)], updated: [new(row.Id, m.A, 12m, row.UpdatedAt)]), "kim", None);

        await act.Should().ThrowAsync<ConcurrencyException>();
        (await Service.SearchAsync(m.Group, None)).Select(r => (r.ItemCd, r.PurchasePrc)).Should().Equal((m.A, 11m));
    }

    private static PurchasePriceRowRequest Row(string itemCd, decimal price) => new(null, itemCd, price, null);

    private static BatchRequest<PurchasePriceRowRequest, PurchasePriceDeleteRequest> Batch(
        PurchasePriceRowRequest[]? added = null, PurchasePriceRowRequest[]? updated = null, PurchasePriceDeleteRequest[]? deleted = null) =>
        new(added ?? [], updated ?? [], deleted ?? []);

    private sealed record Masters(string Group, string A, string B, string C, string Service, string Unused);

    // A new price group, three inventory items (A has standard price 7.25), a service item and an inventory item not in use
    private async Task<Masters> MastersAsync()
    {
        var key = Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        var m = new Masters("G" + key, "A" + key, "B" + key, "C" + key, "S" + key, "U" + key);
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.ExecuteAsync(
            """
            INSERT INTO bc_item (item_cd, item_name, type, valid_flag, usd_prc, created_by, updated_by) VALUES
                (@A, 'Bolt', 'INVENTORY', 'Y', 7.25, 't', 't'),
                (@B, 'Nut', 'INVENTORY', 'Y', NULL, 't', 't'),
                (@C, 'Washer', 'INVENTORY', NULL, NULL, 't', 't'),
                (@Service, 'Freight', 'SERVICE', 'Y', NULL, 't', 't'),
                (@Unused, 'Old bolt', 'INVENTORY', 'N', NULL, 't', 't')
            """,
            m);
        return m;
    }
}
