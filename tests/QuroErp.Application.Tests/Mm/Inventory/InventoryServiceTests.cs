using System.Data;
using FluentAssertions;
using QuroErp.Application.Common;
using QuroErp.Application.Common.DocumentNumbers;
using QuroErp.Application.Tests.Common.DocumentNumbers;
using QuroErp.Application.Mm.Inventory;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Inventory;
using QuroErp.Domain.Mm.Lots;

namespace QuroErp.Application.Tests.Mm.Inventory;

/// <summary>
/// InventoryService with in-memory repositories: on-hand per item + warehouse, ledger, cost and sets
/// (MM-BR-001~008, MM-BR-601~603, MM-BR-606) and lots (MM-BR-352~356).
/// </summary>
public class InventoryServiceTests
{
    private const long Main = 1;
    private const long Second = 2;
    private static readonly DateOnly Day = new(2026, 10, 10);

    private readonly FakeInventoryRepository _inventory = new();
    private readonly FakeSettings _settings = new();
    private readonly FakeSetComponents _sets = new();
    private readonly FakeDocumentNumbers _numbers = new();

    private InventoryService Service => new(_inventory, _settings, _sets,
        new DocumentNumberService(_numbers));

    private Task<IReadOnlyList<GoodsMovement>> Post(params StockMovementRequest[] requests) =>
        Service.PostAsync(requests, "kim", null!, CancellationToken.None);

    [Fact, Trait("Id", "MM-UT-001")]
    public async Task PostAsync_DenyIssueAboveWarehouseOnHand_ThrowsAndSavesNothing()
    {
        _inventory.AddItem(10, "A", onHand: (Main, 5m), cost: 2);
        _inventory.AddOnHand(10, Second, 100);

        var act = () => Post(new StockMovementRequest("DN", Day, 10, Main, -6));

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be("MM-E-001");
        error.Args.Should().Equal("A", "WH1", 5m, 6m, "");
        _inventory.Movements.Should().BeEmpty();
        _inventory.OnHand[(10, Main)].Should().Be(5);
    }

    [Fact, Trait("Id", "MM-UT-002")]
    public async Task PostAsync_AllowInMonthIssueAboveTheDayBalance_SavesAndFlagsMovement()
    {
        _settings.Mode = "ALLOW_IN_MONTH";
        _inventory.AddItem(10, "A", onHand: (Main, 5m), cost: 2);
        // The month must still end at zero or above (MM-BR-010)
        await Post(new StockMovementRequest("GR", new DateOnly(2026, 10, 20), 10, Main, 5, Price: 2));

        var posted = await Post(new StockMovementRequest("DN", Day, 10, Main, -6));

        posted.Should().ContainSingle().Which.NegativeStock.Should().BeTrue();
        _inventory.OnHand[(10, Main)].Should().Be(4);
        _inventory.Items[10].OnHandQty.Should().Be(4);
    }

    [Fact, Trait("Id", "MM-UT-003")]
    public async Task PostAsync_NoSettingRow_IsDeny()
    {
        _settings.Mode = null;
        _inventory.AddItem(10, "A", onHand: (Main, 1m), cost: 2);

        var act = () => Post(new StockMovementRequest("GI", Day, 10, Main, -2));

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("MM-E-001");
    }

    [Fact, Trait("Id", "MM-UT-004")]
    public async Task PostAsync_DateInClosedMonth_ThrowsMmE002()
    {
        _inventory.CloseYearMonth = "202610";
        _inventory.AddItem(10, "A", onHand: (Main, 5m), cost: 2);

        var act = () => Post(new StockMovementRequest("GR", new DateOnly(2026, 9, 30), 10, Main, 1, Price: 3));

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("MM-E-002");
        _inventory.Movements.Should().BeEmpty();
    }

    [Fact, Trait("Id", "MM-UT-005")]
    public async Task PostAsync_ReceiptThenIssue_IssueUsesTheNewMovingAverageCost()
    {
        _inventory.AddItem(10, "A", onHand: (Main, 10m), cost: 10);

        var posted = await Post(
            new StockMovementRequest("GR", Day, 10, Main, 10, Price: 20, RefKey: "GR-1", RefSeq: 1),
            new StockMovementRequest("DN", Day, 10, Main, -4, RefKey: "DN-1", RefSeq: 1));

        posted.Select(m => (m.Prc, m.Amount)).Should().Equal((20m, 200m), (15m, -60m));
        var item = _inventory.Items[10];
        (item.OnHandQty, item.StockValue, item.MovingAverageCost).Should().Be((16m, 240m, 15m));
    }

    [Fact, Trait("Id", "MM-UT-007")]
    public async Task PostAsync_SetIssue_ReducesEachComponentByQtyTimesComponentQty()
    {
        _inventory.AddItem(90, "SET", type: "SET");
        _inventory.AddItem(1, "P1", onHand: (Main, 20m), cost: 3);
        _inventory.AddItem(2, "P2", onHand: (Main, 20m), cost: 5);
        _sets.Components.AddRange([new SetComponent(90, 1, 2), new SetComponent(90, 2, 1)]);

        var posted = await Post(new StockMovementRequest("DN", Day, 90, Main, -3, RefKey: "DN-1", RefSeq: 1));

        posted.Select(m => (m.ItemId, m.Qty, m.Amount)).Should().Equal((1L, -6m, -18m), (2L, -3m, -15m));
        _inventory.OnHand[(1, Main)].Should().Be(14);
        _inventory.OnHand[(2, Main)].Should().Be(17);
        _inventory.OnHand.Should().NotContainKey((90, Main), "a set keeps no stock");
    }

    [Fact, Trait("Id", "CORE-011-T01")]
    public async Task PostAsync_SetLine_LocksItemSetAndComponentsInOnePassAndRemembersTheSet()
    {
        _inventory.AddItem(90, "SET", type: "SET");
        _inventory.AddItem(1, "P1", onHand: (Main, 20m), cost: 3);
        _inventory.AddItem(2, "P2", onHand: (Main, 20m), cost: 5);
        _inventory.AddItem(50, "A", onHand: (Main, 20m), cost: 1);
        _sets.Components.AddRange([new SetComponent(90, 2, 1), new SetComponent(90, 1, 2)]);

        var posted = await Post(
            new StockMovementRequest("DN", Day, 50, Main, -1),
            new StockMovementRequest("DN", Day, 90, Main, -1));

        _inventory.LockedItemCalls.Should().ContainSingle().Which.Should().BeEquivalentTo([1L, 2L, 50L, 90L]);
        posted.Select(m => (m.ItemId, m.SetItemId)).Should().Equal((50L, null), (2L, 90L), (1L, 90L));
    }

    [Fact, Trait("Id", "SD-UT-509")]
    public async Task PostAsync_SetWithoutComponents_ThrowsSdE509AndSavesNothing()
    {
        _inventory.AddItem(90, "SET", type: "SET");

        var act = () => Post(new StockMovementRequest("DN", Day, 90, Main, -1));

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be("SD-E-509");
        error.Args.Should().Equal("SET");
        _inventory.Movements.Should().BeEmpty();
    }

    [Fact, Trait("Id", "CORE-011-T02")]
    public async Task PostAsync_ComponentsChangedBeforeTheLock_ThrowsConcurrency()
    {
        _inventory.AddItem(90, "SET", type: "SET");
        _inventory.AddItem(1, "P1", onHand: (Main, 20m), cost: 3);
        _inventory.AddItem(2, "P2", onHand: (Main, 20m), cost: 5);
        _sets.Components.Add(new SetComponent(90, 1, 2));
        _sets.AfterFirstRead = () => _sets.Components.Add(new SetComponent(90, 2, 1));

        var act = () => Post(new StockMovementRequest("DN", Day, 90, Main, -1));

        await act.Should().ThrowAsync<ConcurrencyException>();
        _inventory.Movements.Should().BeEmpty();
    }

    [Fact, Trait("Id", "MM-UT-007")]
    public async Task PostAsync_SetComponentShortInDeny_ThrowsWithTheSetCode()
    {
        _inventory.AddItem(90, "SET", type: "SET");
        _inventory.AddItem(1, "P1", onHand: (Main, 20m), cost: 3);
        _inventory.AddItem(2, "P2", onHand: (Main, 1m), cost: 5);
        _sets.Components.AddRange([new SetComponent(90, 1, 2), new SetComponent(90, 2, 1)]);

        var act = () => Post(new StockMovementRequest("DN", Day, 90, Main, -3));

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be("MM-E-001");
        error.Args.Should().Equal("P2", "WH1", 1m, 3m, "SET");
    }

    [Fact, Trait("Id", "MM-UT-601")]
    public async Task ReverseAsync_SetComponentMovement_KeepsTheSetAndNamesItInErrors()
    {
        _inventory.AddItem(90, "SET", type: "SET");
        _inventory.AddItem(1, "P1", cost: 3);
        _sets.Components.Add(new SetComponent(90, 1, 2));
        var receipt = (await Post(new StockMovementRequest("GR", Day, 90, Main, 2, Price: 3))).Single();
        await Post(new StockMovementRequest("DN", Day, 1, Main, -1));

        var act = () => Service.ReverseAsync([receipt.Id], Day, "kim", null!, CancellationToken.None);

        receipt.SetItemId.Should().Be(90);
        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be("MM-E-001");
        error.Args.Should().Equal("P1", "WH1", 3m, 4m, "SET");
    }

    [Fact, Trait("Id", "MM-UT-008")]
    public async Task PostAsync_ServiceItem_MakesNoMovement()
    {
        _inventory.AddItem(10, "FEE", type: "SERVICE");

        var posted = await Post(new StockMovementRequest("DN", Day, 10, Main, -1, Price: 50));

        posted.Should().BeEmpty();
        _inventory.Movements.Should().BeEmpty();
    }

    [Fact, Trait("Id", "MM-UT-601")]
    public async Task ReverseAsync_AddsOppositeMovementWithParentAndNewNumber()
    {
        _inventory.AddItem(10, "A", onHand: (Main, 0m), cost: 0);
        var receipt = (await Post(new StockMovementRequest("GR", Day, 10, Main, 4, Price: 5, RefKey: "GR-1", RefSeq: 2))).Single();

        var reversal = (await Service.ReverseAsync([receipt.Id], new DateOnly(2026, 10, 20), "kim", null!, CancellationToken.None)).Single();

        (reversal.MovType, reversal.Qty, reversal.Amount, reversal.ParentMovementId).Should().Be(("GR", -4m, -20m, receipt.Id));
        reversal.DocumentDt.Should().Be(new DateOnly(2026, 10, 20));
        reversal.DocumentNo.Should().Be("ID26100000000002");
        receipt.DocumentNo.Should().Be("ID26100000000001");
        _inventory.OnHand[(10, Main)].Should().Be(0);
    }

    [Fact, Trait("Id", "MM-UT-602")]
    public async Task ReverseAsync_ReceiptReversalBelowOnHandInDeny_ThrowsMmE001()
    {
        _inventory.AddItem(10, "A", onHand: (Main, 0m), cost: 0);
        var receipt = (await Post(new StockMovementRequest("GR", Day, 10, Main, 4, Price: 5))).Single();
        await Post(new StockMovementRequest("DN", Day, 10, Main, -3));

        var act = () => Service.ReverseAsync([receipt.Id], Day, "kim", null!, CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("MM-E-001");
    }

    [Fact, Trait("Id", "MM-UT-602")]
    public async Task ReverseAsync_IssueReversalIncreasesStock_IsNotChecked()
    {
        _inventory.AddItem(10, "A", onHand: (Main, 3m), cost: 2);
        var issue = (await Post(new StockMovementRequest("GI", Day, 10, Main, -3))).Single();

        var reversal = (await Service.ReverseAsync([issue.Id], Day, "kim", null!, CancellationToken.None)).Single();

        reversal.Qty.Should().Be(3);
        _inventory.OnHand[(10, Main)].Should().Be(3);
    }

    [Fact, Trait("Id", "MM-UT-603")]
    public async Task PostAsync_LocksOnHandRowsInItemWarehouseOrderBeforeChecking()
    {
        _inventory.AddItem(1, "A", onHand: (Main, 5m), cost: 1);
        _inventory.AddItem(2, "B", onHand: (Main, 5m), cost: 1);
        _inventory.AddOnHand(1, Second, 5);

        await Post(
            new StockMovementRequest("DN", Day, 2, Main, -1),
            new StockMovementRequest("DN", Day, 1, Second, -1),
            new StockMovementRequest("DN", Day, 1, Main, -1));

        _inventory.Locked.Should().Equal((1L, Main), (1L, Second), (2L, Main));
    }

    [Theory, Trait("Id", "MM-UT-606")]
    [InlineData("GR", -1)]
    [InlineData("DN", 1)]
    [InlineData("GI", 0)]
    public async Task PostAsync_QtySignNotAllowedForType_ThrowsMmE606(string type, int qty)
    {
        _inventory.AddItem(10, "A", onHand: (Main, 5m), cost: 1);

        var act = () => Post(new StockMovementRequest(type, Day, 10, Main, qty, Price: 1));

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("MM-E-606");
    }

    [Fact, Trait("Id", "MM-UT-606")]
    public async Task ReverseAsync_AlreadyReversedMovement_ThrowsMmE606()
    {
        _inventory.AddItem(10, "A", onHand: (Main, 0m), cost: 0);
        var receipt = (await Post(new StockMovementRequest("GR", Day, 10, Main, 4, Price: 5))).Single();
        await Service.ReverseAsync([receipt.Id], Day, "kim", null!, CancellationToken.None);

        var act = () => Service.ReverseAsync([receipt.Id], Day, "kim", null!, CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("MM-E-606");
    }

    [Fact, Trait("Id", "MM-UT-606")]
    public async Task ReverseAsync_SameMovementTwiceInOneCall_ThrowsMmE606()
    {
        _inventory.AddItem(10, "A", onHand: (Main, 0m), cost: 0);
        var receipt = (await Post(new StockMovementRequest("GR", Day, 10, Main, 4, Price: 5))).Single();

        var act = () => Service.ReverseAsync([receipt.Id, receipt.Id], Day, "kim", null!, CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("MM-E-606");
    }

    [Fact, Trait("Id", "MM-INV-T01")]
    public async Task PostAsync_ItemOnHandIsTheSumOfWarehouses()
    {
        _inventory.AddItem(10, "A", onHand: (Main, 0m), cost: 0);

        await Post(
            new StockMovementRequest("GR", Day, 10, Main, 4, Price: 5),
            new StockMovementRequest("GR", Day, 10, Second, 6, Price: 5),
            new StockMovementRequest("DN", Day, 10, Second, -1));

        _inventory.OnHand[(10, Main)].Should().Be(4);
        _inventory.OnHand[(10, Second)].Should().Be(5);
        _inventory.Items[10].OnHandQty.Should().Be(9);
    }

    [Fact, Trait("Id", "MM-UT-607"), Trait("Rule", "MM-BR-607")]
    public async Task PostAsync_MovementNumberUsesTheDocumentDateMonthWithOneSequencePerMonth()
    {
        _inventory.AddItem(10, "A", onHand: (Main, 0m), cost: 0);

        var first = await Post(
            new StockMovementRequest("GR", new DateOnly(2026, 10, 10), 10, Main, 1, Price: 1),
            new StockMovementRequest("GR", new DateOnly(2026, 9, 30), 10, Main, 1, Price: 1));
        var second = await Post(
            new StockMovementRequest("GR", new DateOnly(2026, 9, 1), 10, Main, 1, Price: 1),
            new StockMovementRequest("GR", new DateOnly(2026, 10, 31), 10, Main, 1, Price: 1));

        first.Concat(second).Select(m => m.DocumentNo).Should().Equal(
            "ID26100000000001", "ID26090000000001", "ID26090000000002", "ID26100000000002");
        _numbers.Last.Keys.Should().BeEquivalentTo([("ID", "2609"), ("ID", "2610")]);
    }

    [Fact, Trait("Id", "MM-UT-005")]
    public async Task PostAsync_StockCountIncrease_RecalculatesCostLikeAReceipt()
    {
        _inventory.AddItem(10, "A", onHand: (Main, 10m), cost: 10);

        await Post(new StockMovementRequest("CT", Day, 10, Main, 10, Price: 20));
        var afterIncrease = _inventory.Items[10].MovingAverageCost;
        await Post(new StockMovementRequest("CT", Day, 10, Main, -5, Price: 99));

        afterIncrease.Should().Be(15);
        (_inventory.Items[10].OnHandQty, _inventory.Items[10].MovingAverageCost).Should().Be((15m, 15m));
    }

    [Fact, Trait("Id", "MM-UT-603")]
    public async Task PostAsync_DenyIssueAboveOnHandAndALaterDay_OnHandCheckComesFirst()
    {
        _inventory.AddItem(10, "A", onHand: (Main, 0m), cost: 1);
        await Post(
            new StockMovementRequest("GR", new DateOnly(2026, 10, 1), 10, Main, 5, Price: 1),
            new StockMovementRequest("DN", new DateOnly(2026, 10, 20), 10, Main, -4));

        // On-hand is 1 and 10/5 has 5, but 10/20 would drop to -2
        var act = () => Post(new StockMovementRequest("DN", new DateOnly(2026, 10, 5), 10, Main, -3));

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be("MM-E-001");
    }

    [Fact, Trait("Id", "MM-UT-009")]
    public async Task PostAsync_DenyBackDatedIssueBeforeTheStockArrived_ThrowsMmE009()
    {
        _inventory.AddItem(10, "A", onHand: (Main, 0m), cost: 1);
        await Post(new StockMovementRequest("GR", new DateOnly(2026, 10, 10), 10, Main, 5, Price: 1));

        var act = () => Post(new StockMovementRequest("DN", new DateOnly(2026, 10, 5), 10, Main, -2));

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be("MM-E-009");
        error.Args.Should().Equal("A", "WH1", "2026-10-05", -2m, "");
    }

    [Fact, Trait("Id", "MM-UT-010")]
    public async Task PostAsync_AllowInMonthIssueNegativeOnlyMidMonth_IsSavedAndFlagged()
    {
        _settings.Mode = "ALLOW_IN_MONTH";
        _inventory.AddItem(10, "A", onHand: (Main, 0m), cost: 1);
        await Post(new StockMovementRequest("GR", new DateOnly(2026, 10, 20), 10, Main, 5, Price: 1));

        var issue = (await Post(new StockMovementRequest("DN", new DateOnly(2026, 10, 5), 10, Main, -3))).Single();

        issue.NegativeStock.Should().BeTrue("10/5 to 10/19 is negative (MM-BR-002)");
        _inventory.OnHand[(10, Main)].Should().Be(2);
    }

    [Fact, Trait("Id", "MM-UT-010")]
    public async Task PostAsync_AllowInMonthIssueLeavingAMonthEndNegative_ThrowsMmE010()
    {
        _settings.Mode = "ALLOW_IN_MONTH";
        _inventory.AddItem(10, "A", onHand: (Main, 0m), cost: 1);
        await Post(new StockMovementRequest("GR", new DateOnly(2026, 11, 3), 10, Main, 5, Price: 1));

        var act = () => Post(new StockMovementRequest("DN", new DateOnly(2026, 10, 5), 10, Main, -3));

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be("MM-E-010");
        error.Args.Should().Equal("A", "WH1", "202610", -3m, "");
    }

    [Fact, Trait("Id", "MM-UT-603")]
    public async Task PostAsync_SecondLineOfTheSameDocument_SeesTheFirstLineInPointInTimeCheck()
    {
        _inventory.AddItem(10, "A", onHand: (Main, 0m), cost: 1);
        await Post(new StockMovementRequest("GR", new DateOnly(2026, 10, 1), 10, Main, 5, Price: 1));
        await Post(new StockMovementRequest("GR", new DateOnly(2026, 10, 20), 10, Main, 5, Price: 1));

        // On-hand 10 covers both lines, but on 10/5 only 5 exists
        var act = () => Post(
            new StockMovementRequest("DN", new DateOnly(2026, 10, 5), 10, Main, -3),
            new StockMovementRequest("DN", new DateOnly(2026, 10, 6), 10, Main, -3));

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be("MM-E-009");
        error.Args.Should().Equal("A", "WH1", "2026-10-06", -1m, "");
    }

    // Lot-managed item 20 "L" since 10/1 (MM-BR-351~356)
    private static readonly DateOnly LotStart = new(2026, 10, 1);

    private void AddLotItem(decimal migrated = 0)
    {
        _inventory.AddItem(20, "L", cost: 2, lotManagedFrom: LotStart);
        if (migrated != 0)
        {
            _inventory.AddLotStock(20, Main, LotRules.Migrated, migrated);
        }
    }

    [Fact, Trait("Id", "MM-UT-353"), Trait("Rule", "MM-BR-353")]
    public async Task PostAsync_LotIncreaseWithoutNumber_GetsTheNextAutomaticNumberOfTheDocumentDate()
    {
        AddLotItem();

        await Post(new StockMovementRequest("GR", Day, 20, Main, 5, Price: 2));
        var posted = await Post(new StockMovementRequest("GR", Day, 20, Main, 3, Price: 2), new StockMovementRequest("GR", Day, 20, Main, 1, Price: 2));

        _inventory.Lots.Select(l => l.LotNo).Should().Equal("261010-001", "261010-002", "261010-003");
        posted.Select(m => m.LotId).Should().Equal(2, 3);
        _inventory.LotOnHand.Should().Contain((20, Main, 1), 5m).And.Contain((20, Main, 2), 3m).And.Contain((20, Main, 3), 1m);
        _inventory.OnHand[(20, Main)].Should().Be(9);
    }

    [Fact, Trait("Id", "MM-UT-353"), Trait("Rule", "MM-BR-353")]
    public async Task PostAsync_LotIncreaseWithAnExistingNumber_AddsToThatLot()
    {
        AddLotItem();
        await Post(new StockMovementRequest("GR", Day, 20, Main, 5, Price: 2, LotNo: "B-1"));

        await Post(new StockMovementRequest("GR", Day, 20, Main, 2, Price: 2, LotNo: " b-1 "));

        _inventory.Lots.Should().ContainSingle().Which.LotNo.Should().Be("B-1");
        _inventory.LotOnHand[(20, Main, 1)].Should().Be(7);
    }

    [Theory, Trait("Id", "MM-UT-352"), Trait("Rule", "MM-BR-352")]
    [InlineData("A B")]
    [InlineData("MIGRATED")]
    [InlineData("1234567890123456789012345678901")]
    public async Task PostAsync_LotNumberAgainstTheRules_IsMmE352(string lotNo)
    {
        AddLotItem();

        var act = () => Post(new StockMovementRequest("GR", Day, 20, Main, 5, Price: 2, LotNo: lotNo));

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("MM-E-352");
        _inventory.Lots.Should().BeEmpty();
    }

    [Fact, Trait("Id", "MM-UT-355"), Trait("Rule", "MM-BR-355")]
    public async Task PostAsync_LotDecreaseWithoutALot_IsMmE355AndSavesNothing()
    {
        AddLotItem(migrated: 10);

        var act = () => Post(new StockMovementRequest("DN", Day, 20, Main, -1));
        var unknown = () => Post(new StockMovementRequest("DN", Day, 20, Main, -1, LotNo: "NONE"));

        (await act.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal("L");
        (await unknown.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("MM-E-355");
        _inventory.Movements.Should().BeEmpty();
    }

    [Fact, Trait("Id", "MM-UT-356"), Trait("Rule", "MM-BR-356")]
    public async Task PostAsync_LotBalanceShort_IsBlockedEvenWhenNegativeStockIsAllowedInTheMonth()
    {
        _settings.Mode = "ALLOW_IN_MONTH";
        AddLotItem(migrated: 10);
        await Post(new StockMovementRequest("GR", Day, 20, Main, 5, Price: 2, LotNo: "B"));

        // The warehouse has 15, lot B only 5
        var act = () => Post(new StockMovementRequest("DN", Day, 20, Main, -6, LotNo: "B"));

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be("MM-E-356");
        error.Args.Should().Equal("L", "WH1", "B", "2026-10-10", -1m);
        _inventory.LotOnHand[(20, Main, 2)].Should().Be(5);
    }

    [Fact, Trait("Id", "MM-UT-356"), Trait("Rule", "MM-BR-356")]
    public async Task PostAsync_LotIssueDatedBeforeTheLotWasReceived_IsBlockedAtThatDate()
    {
        AddLotItem(migrated: 10);
        await Post(new StockMovementRequest("GR", new DateOnly(2026, 10, 20), 20, Main, 5, Price: 2, LotNo: "B"));

        var act = () => Post(new StockMovementRequest("DN", new DateOnly(2026, 10, 15), 20, Main, -2, LotNo: "B"));

        (await act.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal("L", "WH1", "B", "2026-10-15", -2m);
    }

    [Fact, Trait("Id", "MM-UT-356"), Trait("Rule", "MM-BR-356")]
    public async Task PostAsync_MigratedStock_CountsFromTheDayLotManagementStarted()
    {
        AddLotItem(migrated: 10);

        var posted = await Post(new StockMovementRequest("DN", LotStart, 20, Main, -4, LotNo: "migrated"));
        var before = () => Post(new StockMovementRequest("GR", LotStart.AddDays(-1), 20, Main, 1, Price: 2));

        posted.Single().LotId.Should().Be(1);
        _inventory.LotOnHand[(20, Main, 1)].Should().Be(6);
        (await before.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal("L", "WH1", "260930-001", "2026-09-30", 0m);
    }

    [Fact, Trait("Id", "MM-UT-355"), Trait("Rule", "MM-BR-355")]
    public async Task ReverseAsync_LotItem_UsesTheOriginalLot_OrMigratedForAMovementFromBeforeLotManagement()
    {
        AddLotItem(migrated: 10);
        var receipt = (await Post(new StockMovementRequest("GR", Day, 20, Main, 5, Price: 2, LotNo: "B"))).Single();
        var old = _inventory.AddOldMovement(20, Main, 3);

        var reversals = await Service.ReverseAsync([receipt.Id, old.Id], Day, "kim", null!, CancellationToken.None);

        reversals.Select(r => r.LotId).Should().Equal(2, 1);
        _inventory.LotOnHand[(20, Main, 2)].Should().Be(0);
        _inventory.LotOnHand[(20, Main, 1)].Should().Be(7);
    }

    [Fact, Trait("Id", "MM-UT-354"), Trait("Rule", "MM-BR-354")]
    public async Task PostAsync_LotBalances_AreLockedAfterTheOnHandRows_AndItemsWithoutLotsIgnoreTheLot()
    {
        AddLotItem(migrated: 10);
        _inventory.AddItem(10, "A", onHand: (Main, 5m), cost: 2);

        var posted = await Post(
            new StockMovementRequest("DN", Day, 20, Main, -1, LotNo: LotRules.Migrated),
            new StockMovementRequest("DN", Day, 10, Main, -1, LotNo: "X"));

        _inventory.LockLog.Should().Equal("onhand 10/1", "onhand 20/1", "lot 20/1/1");
        posted.Select(m => m.LotId).Should().Equal(1, null);
        _inventory.Lots.Should().ContainSingle();
    }

    private sealed class FakeInventoryRepository : IInventoryRepository
    {
        public Task<IReadOnlyDictionary<long, decimal>> GetSellableOnHandAsync(
            IReadOnlyCollection<long> itemIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public string? CloseYearMonth { get; set; }

        public Dictionary<long, ItemStock> Items { get; } = [];

        public Dictionary<(long ItemId, long WarehouseId), decimal> OnHand { get; } = [];

        public List<GoodsMovement> Movements { get; } = [];

        public List<(long ItemId, long WarehouseId)> Locked { get; } = [];

        public List<long[]> LockedItemCalls { get; } = [];

        /// <summary>Starting stock, read as the closing balance of a closed month (keeps ledger = on-hand, MM-BR-605).</summary>
        public Dictionary<(long ItemId, long WarehouseId), decimal> Opening { get; } = [];

        public void AddItem(
            long id, string itemCd, (long WarehouseId, decimal Qty)? onHand = null, decimal cost = 0, string type = "INVENTORY",
            DateOnly? lotManagedFrom = null)
        {
            var qty = onHand?.Qty ?? 0;
            Items[id] = new ItemStock(id, itemCd, type, qty, qty * cost, cost, lotManagedFrom is not null, lotManagedFrom);
            if (onHand is { } o)
            {
                OnHand[(id, o.WarehouseId)] = o.Qty;
                Opening[(id, o.WarehouseId)] = o.Qty;
            }
        }

        public void AddOnHand(long itemId, long warehouseId, decimal qty)
        {
            OnHand[(itemId, warehouseId)] = qty;
            Opening[(itemId, warehouseId)] = qty;
            var item = Items[itemId];
            Items[itemId] = new ItemStock(
                itemId, item.ItemCd, item.Type, item.OnHandQty + qty, item.StockValue + qty * item.MovingAverageCost, item.MovingAverageCost,
                item.LotManaged, item.LotManagedFrom);
        }

        public List<LotRef> Lots { get; } = [];

        private readonly Dictionary<long, long> _lotItems = [];

        public Dictionary<(long ItemId, long WarehouseId, long LotId), decimal> LotOnHand { get; } = [];

        public List<string> LockLog { get; } = [];

        public void AddLotStock(long itemId, long warehouseId, string lotNo, decimal qty)
        {
            var lot = Ensure(itemId, lotNo);
            LotOnHand[(itemId, warehouseId, lot.Id)] = qty;
            AddOnHand(itemId, warehouseId, qty);
        }

        /// <summary>A movement saved before lot management was turned on: no lot, already in the MIGRATED balance.</summary>
        public GoodsMovement AddOldMovement(long itemId, long warehouseId, decimal qty)
        {
            var movement = GoodsMovement.Restore(Movements.Count + 1, "OLD", "GR", new DateOnly(2026, 9, 1), itemId, warehouseId, qty, 2, null, null, null, false);
            Movements.Add(movement);
            return movement;
        }

        public Task<LotRef?> FindLotAsync(long itemId, long? lotId, string? lotNo, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(Lots.FirstOrDefault(l => _lotItems[l.Id] == itemId && (lotId is { } id ? l.Id == id : l.LotNo == lotNo)));

        public Task<LotRef> EnsureLotAsync(long itemId, string lotNo, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(Ensure(itemId, lotNo));

        public Task<int> GetLastAutoLotSeqAsync(long itemId, string prefix, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(Lots.Where(l => _lotItems[l.Id] == itemId && l.LotNo.StartsWith(prefix, StringComparison.Ordinal)
                    && int.TryParse(l.LotNo[prefix.Length..], out _))
                .Select(l => int.Parse(l.LotNo[prefix.Length..], System.Globalization.CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Max());

        public Task<decimal> LockLotOnHandAsync(long itemId, long warehouseId, long lotId, string user, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            LockLog.Add($"lot {itemId}/{warehouseId}/{lotId}");
            return Task.FromResult(LotOnHand.GetValueOrDefault((itemId, warehouseId, lotId)));
        }

        public Task UpdateLotOnHandAsync(
            long itemId, long warehouseId, long lotId, decimal onHandQty, string user, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            LotOnHand[(itemId, warehouseId, lotId)] = onHandQty;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LotDailyQty>> GetLotDailyQtyAsync(
            long itemId, long warehouseId, long lotId, DateOnly fromDate, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LotDailyQty>>(Movements
                .Where(m => m.ItemId == itemId && m.WarehouseId == warehouseId && m.LotId == lotId && m.DocumentDt >= fromDate)
                .GroupBy(m => m.DocumentDt).OrderBy(g => g.Key).Select(g => new LotDailyQty(g.Key, g.Sum(m => m.Qty))).ToList());

        private LotRef Ensure(long itemId, string lotNo)
        {
            if (Lots.FirstOrDefault(l => _lotItems[l.Id] == itemId && l.LotNo == lotNo) is { } found)
            {
                return found;
            }
            var lot = new LotRef(Lots.Count + 1, lotNo);
            Lots.Add(lot);
            _lotItems[lot.Id] = itemId;
            return lot;
        }

        public Task<string?> GetCloseYearMonthAsync(IDbTransaction transaction, CancellationToken cancellationToken) => Task.FromResult(CloseYearMonth);

        // Copies, so a failed post does not change the stored items
        public Task<IReadOnlyList<ItemStock>> LockItemsAsync(IReadOnlyCollection<long> itemIds, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            LockedItemCalls.Add([.. itemIds]);
            return Task.FromResult<IReadOnlyList<ItemStock>>(itemIds.Where(Items.ContainsKey).Select(id => Items[id])
                .Select(i => new ItemStock(i.ItemId, i.ItemCd, i.Type, i.OnHandQty, i.StockValue, i.MovingAverageCost, i.LotManaged, i.LotManagedFrom)).ToList());
        }

        public Task<OnHandRow> LockOnHandAsync(long itemId, long warehouseId, string user, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Locked.Add((itemId, warehouseId));
            LockLog.Add($"onhand {itemId}/{warehouseId}");
            return Task.FromResult(new OnHandRow(itemId, warehouseId, $"WH{warehouseId}", OnHand.GetValueOrDefault((itemId, warehouseId))));
        }

        public Task UpdateOnHandAsync(long itemId, long warehouseId, decimal onHandQty, string user, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            OnHand[(itemId, warehouseId)] = onHandQty;
            return Task.CompletedTask;
        }

        public Task UpdateItemStockAsync(ItemStock item, string user, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Items[item.ItemId] = item;
            return Task.CompletedTask;
        }

        public Task InsertMovementAsync(GoodsMovement movement, string user, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            movement.Id = Movements.Count + 1;
            Movements.Add(movement);
            return Task.CompletedTask;
        }

        // Same result as the SQL in InventoryRepository, without closed months
        public Task<(decimal Opening, IReadOnlyList<DailyStockBalance> Balances)> GetDailyBalancesAsync(
            long itemId, long warehouseId, DateOnly? lastClosedMonthEnd, DateOnly movementDate, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            var days = Movements.Where(m => m.ItemId == itemId && m.WarehouseId == warehouseId)
                .GroupBy(m => m.DocumentDt).OrderBy(g => g.Key).ToList();
            var opening = Opening.GetValueOrDefault((itemId, warehouseId));
            var running = opening;
            var all = days.Select((g, i) => new DailyStockBalance(g.Key, running += g.Sum(m => m.Qty),
                i == days.Count - 1 || days[i + 1].Key.Month != g.Key.Month || days[i + 1].Key.Year != g.Key.Year)).ToList();
            var start = all.LastOrDefault(b => b.Date <= movementDate)?.Date ?? movementDate;
            return Task.FromResult<(decimal, IReadOnlyList<DailyStockBalance>)>((opening, all.Where(b => b.Date >= start).ToList()));
        }

        public Task<IReadOnlyList<SavedMovement>> LockMovementsAsync(IReadOnlyCollection<long> movementIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SavedMovement>>(Movements.Where(m => movementIds.Contains(m.Id))
                .Select(m => new SavedMovement(m, Movements.Any(r => r.ParentMovementId == m.Id))).ToList());
    }

    private sealed class FakeSettings : IInventorySettingRepository
    {
        public string? Mode { get; set; } = "DENY";

        public Task<InventorySettingRow?> GetAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(Mode is null ? null : new InventorySettingRow(Mode, "DENY", false, DateTimeOffset.UnixEpoch, "system"));

        public Task<InventorySettingRow?> UpdateAsync(
            string negativeStockMode, string overReceiptMode, bool allowCostAdjustmentEdit, DateTimeOffset expectedUpdatedAt, string updatedBy,
            IDbTransaction transaction, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeSetComponents : ISetComponentRepository
    {
        public List<SetComponent> Components { get; } = [];

        /// <summary>Runs once after the first read, like another user saving the set's components meanwhile.</summary>
        public Action? AfterFirstRead { get; set; }

        public Task<IReadOnlyList<SetComponent>> GetComponentsAsync(IReadOnlyCollection<long> setItemIds, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            IReadOnlyList<SetComponent> read = Components.Where(c => setItemIds.Contains(c.SetItemId)).ToList();
            AfterFirstRead?.Invoke();
            AfterFirstRead = null;
            return Task.FromResult(read);
        }
    }
}
