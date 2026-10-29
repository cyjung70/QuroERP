using Dapper;
using FluentAssertions;
using Npgsql;
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Mm.Inventory;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Mm.Inventory;

/// <summary>
/// mm_inventory_setting after 0211_mm_inventory_setting_negative_stock_mode.sql (MM-BR-003) and its repository,
/// real PostgreSQL. Each test rolls back.
/// </summary>
[Collection(AuthSchemaDefinition.Name)]
public class InventorySettingRepositoryTests(AuthSchemaFixture db)
{
    [DatabaseFact, Trait("Id", "MM-SET-IT-01")]
    public async Task Migration_LeavesOneDenyRowWithOnlyTheNewColumns()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);
        var connection = uow.Transaction.Connection!;

        var row = await db.Get<IInventorySettingRepository>().GetAsync(uow.Transaction, CancellationToken.None);
        var count = await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM mm_inventory_setting", transaction: uow.Transaction);
        var columns = await connection.QueryAsync<string>(
            """
            SELECT column_name FROM information_schema.columns
            WHERE table_schema = current_schema() AND table_name = 'mm_inventory_setting'
            """,
            transaction: uow.Transaction);

        row!.NegativeStockMode.Should().Be("DENY");
        row.UpdatedBy.Should().Be("system");
        count.Should().Be(1);
        columns.Should().BeEquivalentTo("id", "negative_stock_mode", "created_at", "created_by", "updated_at", "updated_by");
    }

    [DatabaseFact, Trait("Id", "MM-SET-IT-02")]
    public async Task UpdateAsync_CurrentUpdatedAt_SavesModeAndReturnsNewRow()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);
        var settings = db.Get<IInventorySettingRepository>();
        var current = await settings.GetAsync(uow.Transaction, CancellationToken.None);

        var updated = await settings.UpdateAsync("ALLOW_IN_MONTH", current!.UpdatedAt, "kim", uow.Transaction, CancellationToken.None);
        var reread = await settings.GetAsync(uow.Transaction, CancellationToken.None);

        updated.Should().NotBeNull();
        updated!.NegativeStockMode.Should().Be("ALLOW_IN_MONTH");
        updated.UpdatedBy.Should().Be("kim");
        updated.UpdatedAt.Should().BeAfter(current.UpdatedAt);
        reread.Should().Be(updated);
    }

    [DatabaseFact, Trait("Id", "MM-SET-IT-03")]
    public async Task UpdateAsync_StaleUpdatedAt_ReturnsNullAndChangesNothing()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);
        var settings = db.Get<IInventorySettingRepository>();
        var current = await settings.GetAsync(uow.Transaction, CancellationToken.None);

        var updated = await settings.UpdateAsync("ALLOW", current!.UpdatedAt.AddSeconds(-1), "kim", uow.Transaction, CancellationToken.None);
        var reread = await settings.GetAsync(uow.Transaction, CancellationToken.None);

        updated.Should().BeNull();
        reread.Should().Be(current);
    }

    [DatabaseFact, Trait("Id", "MM-SET-IT-04")]
    public async Task SecondRow_IsRejected()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);

        var act = () => uow.Transaction.Connection!.ExecuteAsync(
            "INSERT INTO mm_inventory_setting (negative_stock_mode, created_by, updated_by) VALUES ('ALLOW', 's', 's')",
            transaction: uow.Transaction);

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [DatabaseFact, Trait("Id", "MM-SET-IT-05")]
    public async Task UnknownMode_IsRejected()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);

        var act = () => uow.Transaction.Connection!.ExecuteAsync(
            "UPDATE mm_inventory_setting SET negative_stock_mode = 'MAYBE'", transaction: uow.Transaction);

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }
}
