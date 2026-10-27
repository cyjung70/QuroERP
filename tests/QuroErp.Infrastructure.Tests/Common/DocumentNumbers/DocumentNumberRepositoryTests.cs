using Dapper;
using FluentAssertions;
using QuroErp.Application.Common.DocumentNumbers;
using QuroErp.Application.Common.Persistence;
using QuroErp.Domain.Common;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Common.DocumentNumbers;

/// <summary>
/// DocumentNumberService on sys_document_number, real PostgreSQL (SD-BR-006). Every test uses its own prefix so
/// tests sharing the schema never share a counter.
/// </summary>
[Collection(AuthSchemaDefinition.Name)]
public class DocumentNumberRepositoryTests(AuthSchemaFixture db)
{
    private static readonly DateOnly Day = new(2026, 10, 27);

    [DatabaseFact, Trait("Id", "CORE-007-IT-01"), Trait("Rule", "SD-BR-006")]
    public async Task NextAsync_ConcurrentTransactions_NeverIssueTheSameNumber()
    {
        var format = NewFormat();

        // The counter row does not exist yet, so the transactions also race to create it
        var batches = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);
            var issued = await Service().NextAsync(format, [Day, Day, Day], "kim", uow.Transaction, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
            return issued;
        }));

        var all = batches.SelectMany(b => b).ToList();
        all.Should().OnlyHaveUniqueItems();
        all.Order(StringComparer.Ordinal).Should().Equal(
            Enumerable.Range(1, 24).Select(i => $"{Head(format)}{i:D5}"));
        batches.Should().OnlyContain(b => b.SequenceEqual(b.Order(StringComparer.Ordinal)), "one transaction holds the counter for all its numbers");
    }

    [DatabaseFact, Trait("Id", "CORE-007-IT-02"), Trait("Rule", "SD-BR-006")]
    public async Task NextAsync_SecondTransaction_WaitsForTheFirstToEnd()
    {
        var format = NewFormat();
        var units = db.Get<IUnitOfWorkFactory>();
        await using var first = await units.BeginAsync(CancellationToken.None);
        var firstNo = await Service().NextAsync(format, Day, "first", first.Transaction, CancellationToken.None);

        var second = Task.Run(async () =>
        {
            await using var uow = await units.BeginAsync(CancellationToken.None);
            var no = await Service().NextAsync(format, Day, "second", uow.Transaction, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
            return no;
        });
        await Task.Delay(500);
        second.IsCompleted.Should().BeFalse("the second transaction waits on the sys_document_number row lock");
        await first.CommitAsync(CancellationToken.None);

        (firstNo, await second).Should().Be(($"{Head(format)}00001", $"{Head(format)}00002"));
    }

    [DatabaseFact, Trait("Id", "CORE-007-IT-03"), Trait("Rule", "SD-BR-006")]
    public async Task NextAsync_RolledBackSave_GivesTheNumberBack()
    {
        var format = NewFormat();
        var units = db.Get<IUnitOfWorkFactory>();
        await using (var failed = await units.BeginAsync(CancellationToken.None))
        {
            await Service().NextAsync(format, Day, "kim", failed.Transaction, CancellationToken.None);
            await failed.RollbackAsync(CancellationToken.None);
        }

        await using var uow = await units.BeginAsync(CancellationToken.None);
        var issued = await Service().NextAsync(format, Day, "kim", uow.Transaction, CancellationToken.None);

        issued.Should().Be($"{Head(format)}00001");
    }

    [DatabaseFact, Trait("Id", "CORE-007-IT-04"), Trait("Rule", "SD-BR-006")]
    public async Task NextAsync_EachPeriodAndContinuousFormat_HasItsOwnRow()
    {
        var monthly = NewFormat();
        var continuous = monthly with { RestartsMonthly = false };
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);

        await Service().NextAsync(monthly, [Day, Day.AddMonths(-1)], "kim", uow.Transaction, CancellationToken.None);
        await Service().NextAsync(continuous, [Day, Day.AddMonths(-1)], "kim", uow.Transaction, CancellationToken.None);

        var rows = await uow.Transaction.Connection!.QueryAsync<(string Period, string TransNo)>(
            "SELECT period, trans_no FROM sys_document_number WHERE trans_type = @Prefix ORDER BY period",
            new { monthly.Prefix }, uow.Transaction);
        rows.Should().Equal(("", $"{monthly.Prefix}00002"), ("2609", $"{monthly.Prefix}260900001"), ("2610", $"{monthly.Prefix}261000001"));
    }

    private static string Head(DocumentNumberFormat format) => format.Prefix + format.Period(Day);

    private static DocumentNumberFormat NewFormat() => new($"T{Guid.NewGuid():N}"[..8], 5, RestartsMonthly: true);

    private DocumentNumberService Service() => new(db.Get<IDocumentNumberRepository>());
}
