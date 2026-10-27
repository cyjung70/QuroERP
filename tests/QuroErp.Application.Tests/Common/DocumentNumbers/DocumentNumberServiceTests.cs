using FluentAssertions;
using QuroErp.Application.Common.DocumentNumbers;
using QuroErp.Domain.Common;

namespace QuroErp.Application.Tests.Common.DocumentNumbers;

/// <summary>DocumentNumberService on a fake counter table (SD-BR-006).</summary>
public class DocumentNumberServiceTests
{
    private static readonly DateOnly Day = new(2026, 10, 27);

    private readonly FakeDocumentNumbers _numbers = new();

    private DocumentNumberService Service => new(_numbers);

    [Fact, Trait("Id", "CORE-007-T02"), Trait("Rule", "SD-BR-006")]
    public async Task NextAsync_ContinuesTheSavedCounter()
    {
        _numbers.Last[("SO", "2610")] = "SO261000041";

        var issued = await Service.NextAsync(DocumentNumberFormat.SalesOrder, Day, "kim", null!, CancellationToken.None);

        issued.Should().Be("SO261000042");
        _numbers.Last[("SO", "2610")].Should().Be("SO261000042");
    }

    [Fact, Trait("Id", "CORE-007-T03"), Trait("Rule", "SD-BR-006")]
    public async Task NextAsync_NewCounter_CreatesItWithTheFirstNumbers()
    {
        var issued = await Service.NextAsync(DocumentNumberFormat.SalesOrder, [Day, Day, Day], "kim", null!, CancellationToken.None);

        issued.Should().Equal("SO261000001", "SO261000002", "SO261000003");
        _numbers.Last[("SO", "2610")].Should().Be("SO261000003");
    }

    [Fact, Trait("Id", "CORE-007-T04"), Trait("Rule", "SD-BR-006")]
    public async Task NextAsync_CounterCreatedByAnotherTransaction_LocksAgainAndContinuesIt()
    {
        _numbers.CreatedByOther = "SO261000001";

        var issued = await Service.NextAsync(DocumentNumberFormat.SalesOrder, Day, "kim", null!, CancellationToken.None);

        issued.Should().Be("SO261000002");
        _numbers.Locked.Should().Equal(("SO", "2610"), ("SO", "2610"));
    }

    [Fact, Trait("Id", "CORE-007-T05"), Trait("Rule", "SD-BR-006")]
    public async Task NextAsync_DatesInTwoMonths_UsesTheDocumentDatePeriodsInOrderAndKeepsInputOrder()
    {
        var issued = await Service.NextAsync(
            DocumentNumberFormat.SalesOrder, [Day, new DateOnly(2026, 9, 30), Day, new DateOnly(2026, 9, 1)],
            "kim", null!, CancellationToken.None);

        issued.Should().Equal("SO261000001", "SO260900001", "SO261000002", "SO260900002");
        _numbers.Locked.Should().Equal(("SO", "2609"), ("SO", "2610"));
    }

    [Fact, Trait("Id", "SD-UT-106"), Trait("Rule", "SD-BR-106")]
    public async Task NextAsync_ContinuousFormat_UsesOneCounterWithoutPeriod()
    {
        var issued = await Service.NextAsync(
            DocumentNumberFormat.SalesInvoice, [Day, new DateOnly(2026, 9, 30)], "kim", null!, CancellationToken.None);

        issued.Should().Equal("DN0000001", "DN0000002");
        _numbers.Locked.Should().Equal(("DN", ""));
    }

    [Fact, Trait("Id", "SD-UT-006"), Trait("Rule", "SD-BR-006")]
    public async Task NextAsync_SequenceOutOfDigits_ThrowsSdE006AndSavesNothing()
    {
        _numbers.Last[("SO", "2610")] = "SO261099999";

        var act = () => Service.NextAsync(DocumentNumberFormat.SalesOrder, Day, "kim", null!, CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("SD-E-006");
        _numbers.Last[("SO", "2610")].Should().Be("SO261099999");
    }
}
