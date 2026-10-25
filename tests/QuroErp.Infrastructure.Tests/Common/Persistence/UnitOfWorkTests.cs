using System.Data;
using Dapper;
using FluentAssertions;
using QuroErp.Application.Common.Persistence;
using QuroErp.Infrastructure.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Common.Persistence;

/// <summary>연결 팩토리·UnitOfWork 통합 테스트 (TASK-CORE-002, 실제 PostgreSQL).</summary>
[Collection(InfrastructureDefinition.Name)]
public class UnitOfWorkTests(InfrastructureFixture db)
{
    private const string Insert = "INSERT INTO uow_probe (id, probe_name) VALUES (@Id, @ProbeName)";
    private const string CountById = "SELECT count(*) FROM uow_probe WHERE id = @Id";

    private static int _nextId;

    [DatabaseFact, Trait("Id", "CORE-002-IT-01")]
    public async Task OpenAsync_ConnectionString_ReturnsOpenConnectionOnSchema()
    {
        await using var connection = await db.Get<IDbConnectionFactory>().OpenAsync(CancellationToken.None);

        connection.State.Should().Be(ConnectionState.Open);
        (await connection.ExecuteScalarAsync<string>("SELECT current_schema()")).Should().Be(db.Schema);
    }

    [DatabaseFact, Trait("Id", "CORE-002-IT-02")]
    public async Task CommitAsync_InsertedRow_IsVisibleToOtherConnection()
    {
        var id = NextId();

        await using (var uow = await BeginAsync())
        {
            await InsertAsync(uow, id);
            await uow.CommitAsync(CancellationToken.None);
        }

        (await CountAsync(id)).Should().Be(1);
    }

    [DatabaseFact, Trait("Id", "CORE-002-IT-03")]
    public async Task RollbackAsync_InsertedRow_IsDiscarded()
    {
        var id = NextId();

        await using (var uow = await BeginAsync())
        {
            await InsertAsync(uow, id);
            await uow.RollbackAsync(CancellationToken.None);
        }

        (await CountAsync(id)).Should().Be(0);
    }

    [DatabaseFact, Trait("Id", "CORE-002-IT-04")]
    public async Task DisposeAsync_WithoutCommit_RollsBack()
    {
        var id = NextId();

        await using (var uow = await BeginAsync())
        {
            await InsertAsync(uow, id);
        }

        (await CountAsync(id)).Should().Be(0);
    }

    [DatabaseFact, Trait("Id", "CORE-002-IT-05")]
    public async Task BeginAsync_BeforeCommit_ChangesAreNotVisibleToOtherConnection()
    {
        var id = NextId();
        await using var uow = await BeginAsync();
        await InsertAsync(uow, id);

        var visibleOutside = await CountAsync(id);
        await uow.CommitAsync(CancellationToken.None);

        visibleOutside.Should().Be(0);
        (await CountAsync(id)).Should().Be(1);
    }

    [DatabaseFact, Trait("Id", "CORE-002-IT-06")]
    public async Task CommitAsync_AfterCommit_Throws()
    {
        await using var uow = await BeginAsync();
        await uow.CommitAsync(CancellationToken.None);

        var act = () => uow.CommitAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [DatabaseFact, Trait("Id", "CORE-002-IT-07")]
    public async Task Query_SnakeCaseColumn_MapsToPascalCaseProperty()
    {
        var id = NextId();
        await using (var uow = await BeginAsync())
        {
            await InsertAsync(uow, id);
            await uow.CommitAsync(CancellationToken.None);
        }
        await using var connection = await db.Get<IDbConnectionFactory>().OpenAsync(CancellationToken.None);

        var row = await connection.QuerySingleAsync<ProbeRow>("SELECT id, probe_name FROM uow_probe WHERE id = @Id", new { Id = id });

        row.ProbeName.Should().Be($"probe-{id}");
    }

    private static int NextId() => Interlocked.Increment(ref _nextId);

    private Task<IUnitOfWork> BeginAsync() => db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);

    private static Task<int> InsertAsync(IUnitOfWork uow, int id) =>
        uow.Transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Insert, new { Id = id, ProbeName = $"probe-{id}" }, uow.Transaction, cancellationToken: CancellationToken.None));

    private async Task<long> CountAsync(int id)
    {
        await using var connection = await db.Get<IDbConnectionFactory>().OpenAsync(CancellationToken.None);
        return await connection.ExecuteScalarAsync<long>(CountById, new { Id = id });
    }

    private sealed class ProbeRow
    {
        public int Id { get; set; }

        public string ProbeName { get; set; } = "";
    }
}
