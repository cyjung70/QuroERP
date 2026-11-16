using FluentAssertions;
using QuroErp.Domain.Mm.Lots;

namespace QuroErp.Domain.Tests.Mm.Lots;

/// <summary>Lot trace rules MM-BR-366~369 (MM-UC-040).</summary>
public class LotTraceRulesTests
{
    private static readonly DateOnly Day = new(2026, 11, 4);
    private static readonly LotTraceLot Received = new(
        1, 10, "CABLE", "Cable", "261104-001", Day, "GR-1", 1, Day, "Vendor A", 10m, null, null, null);
    private static readonly LotTraceLot SetLot = new(
        2, 20, "SET", "Gift Set", "261105-001", Day.AddDays(1), null, null, null, null, null, "AS-1", Day.AddDays(1), 3m);

    [Fact, Trait("Id", "MM-UT-366"), Trait("Rule", "MM-BR-366")]
    public void Net_CancelsAReversalWithItsOriginal_AndJoinsALineSplitOverLocations()
    {
        LotTraceMovement[] movements =
        [
            Move(1, "DN", "SH-1", 1, -2m, Day),
            Move(1, "DN", "SH-1", 1, -1m, Day),
            Move(1, "DN", "SH-2", 1, -4m, Day),
            Move(1, "DN", "SH-2", 1, 4m, Day.AddDays(2)),
        ];

        var net = LotTraceRules.Net(movements);

        net.Select(m => (m.RefKey, m.Qty)).Should().Equal(("SH-1", -3m));
    }

    [Fact, Trait("Id", "MM-UC-040-T01"), Trait("Id", "MM-UT-366"), Trait("Rule", "MM-BR-366")]
    public void Forward_ShowsTheLot_ThenItsReceiptAndShipmentsUnderIt()
    {
        LotTraceMovement[] movements =
        [
            Move(1, "GR", "GR-1", 1, 10m, Day, "GR", "Vendor A"),
            Move(1, "DN", "SH-1", 1, -3m, Day.AddDays(1), "SHIP", "Customer A"),
            Move(1, "DN", "SH-2", 2, -4m, Day.AddDays(2), "SHIP", "Customer B"),
        ];

        var rows = LotTraceRules.Forward(Received, movements, [], []);

        rows.Select(r => (r.Key, r.ParentKey, r.Level, r.DocType, r.DocNo, r.PartyName, r.Qty)).Should().Equal(
            (1, (int?)null, 0, "LOT", "GR-1", "Vendor A", (decimal?)null),
            (2, 1, 1, "GR", "GR-1", "Vendor A", 10m),
            (3, 1, 1, "SHIP", "SH-1", "Customer A", -3m),
            (4, 1, 1, "SHIP", "SH-2", "Customer B", -4m));
    }

    [Theory, Trait("Id", "MM-UT-366"), Trait("Rule", "MM-BR-363")]
    [InlineData("*")]
    [InlineData(" * ")]
    [InlineData("")]
    public void LotStar_IsNotTraced(string lotNo)
    {
        LotTraceRules.IsTraceable(lotNo).Should().BeFalse();
        LotTraceRules.Forward(Received with { LotNo = lotNo }, [Move(1, "GR", "GR-1", 1, 10m, Day)], [], []).Should().BeEmpty();
    }

    [Fact, Trait("Id", "MM-UC-040-T03"), Trait("Id", "MM-UT-368"), Trait("Rule", "MM-BR-368")]
    public void Forward_GoesIntoTheSetLotOfAnAssemblyIssue_OneStep()
    {
        LotTraceMovement[] movements =
        [
            Move(1, "GR", "GR-1", 1, 10m, Day, "GR", "Vendor A"),
            Move(1, "BI", "AS-1", 1, -6m, Day.AddDays(1), "ASM"),
            Move(2, "BP", "AS-1", null, 3m, Day.AddDays(1), "ASM", item: "SET", lotNo: "261105-001"),
            Move(2, "DN", "SH-9", 1, -2m, Day.AddDays(2), "SHIP", "Customer B", item: "SET", lotNo: "261105-001"),
        ];
        LotTraceLink[] links = [new(2, 1, "AS-1", Day.AddDays(1), 6m)];

        var rows = LotTraceRules.Forward(Received, movements, links, [SetLot]);

        rows.Select(r => (r.Key, r.ParentKey, r.Level, r.DocType, r.ItemCd, r.Qty)).Should().Equal(
            (1, (int?)null, 0, "LOT", "CABLE", (decimal?)null),
            (2, 1, 1, "GR", "CABLE", 10m),
            (3, 1, 1, "ASM", "CABLE", -6m),
            (4, 3, 2, "LOT", "SET", 6m),
            (5, 4, 3, "ASM", "SET", 3m),
            (6, 4, 3, "SHIP", "SET", -2m));
        rows[3].DocNo.Should().Be("AS-1");
    }

    [Fact, Trait("Id", "MM-UC-040-T02"), Trait("Id", "MM-UT-367"), Trait("Rule", "MM-BR-367")]
    public void Backward_ShowsEachIssue_ThenTheReceiptItsLotCameFrom()
    {
        LotTraceMovement[] issues =
        [
            Move(1, "TR", "IV-1", 1, -2m, Day.AddDays(1), "INV", "Customer A"),
            Move(3, "DN", "SH-1", 2, -1m, Day.AddDays(1), "SHIP", "Customer A", lotNo: "*"),
            Move(1, "DN", "SH-1", 3, -1m, Day.AddDays(1), "SHIP", "Customer A"),
            Move(1, "DN", "SH-1", 3, 1m, Day.AddDays(2), "SHIP", "Customer A"),
        ];

        var rows = LotTraceRules.Backward(issues, [Received], []);

        rows.Select(r => (r.Key, r.ParentKey, r.Level, r.DocType, r.DocNo, r.PartyName, r.Qty)).Should().Equal(
            (1, (int?)null, 0, "INV", "IV-1", "Customer A", (decimal?)-2m),
            (2, 1, 1, "GR", "GR-1", "Vendor A", 10m));
    }

    [Fact, Trait("Id", "MM-UC-040-T03"), Trait("Id", "MM-UT-368"), Trait("Rule", "MM-BR-368")]
    public void Backward_FromASetLot_ShowsItsAssembly_ThenTheComponentLotsAndTheirReceipts()
    {
        LotTraceMovement[] issues = [Move(2, "DN", "SH-9", 1, -2m, Day.AddDays(2), "SHIP", "Customer B", item: "SET", lotNo: "261105-001")];
        LotTraceLink[] links = [new(2, 1, "AS-1", Day.AddDays(1), 6m)];

        var rows = LotTraceRules.Backward(issues, [Received, SetLot], links);

        rows.Select(r => (r.Key, r.ParentKey, r.Level, r.DocType, r.DocNo, r.ItemCd, r.Qty)).Should().Equal(
            (1, (int?)null, 0, "SHIP", "SH-9", "SET", (decimal?)-2m),
            (2, 1, 1, "ASM", "AS-1", "SET", 3m),
            (3, 2, 2, "GR", "GR-1", "CABLE", 6m));
    }

    [Fact, Trait("Id", "MM-UT-369"), Trait("Rule", "MM-BR-369")]
    public void Rows_AreNumberedInOrder_AndEveryParentComesBeforeItsChildren()
    {
        LotTraceMovement[] movements =
        [
            Move(1, "GR", "GR-1", 1, 10m, Day),
            Move(1, "BI", "AS-1", 1, -6m, Day.AddDays(1), "ASM"),
            Move(2, "BP", "AS-1", null, 3m, Day.AddDays(1), "ASM", item: "SET"),
        ];

        var rows = LotTraceRules.Forward(Received, movements, [new LotTraceLink(2, 1, "AS-1", Day, 6m)], [SetLot]);

        rows.Select(r => r.Key).Should().Equal(Enumerable.Range(1, rows.Count));
        rows.Where(r => r.ParentKey is not null).Should().OnlyContain(r => r.ParentKey < r.Key);
        rows.Where(r => r.ParentKey is not null).Should().OnlyContain(r => rows[r.ParentKey!.Value - 1].Level == r.Level - 1);
    }

    private static LotTraceMovement Move(
        long lotId, string movType, string refKey, int? refSeq, decimal qty, DateOnly date, string docType = "MOVE", string? party = null,
        string item = "CABLE", string lotNo = "261104-001") =>
        new(lotId, movType, refKey, refSeq, date, 1, "MAIN", lotId == 2 ? 20 : 10, item, null, lotNo, qty, docType, party);
}
