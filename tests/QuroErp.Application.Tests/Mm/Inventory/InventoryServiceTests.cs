using System.Data;
using FluentAssertions;
using QuroErp.Application.Common;
using QuroErp.Application.Common.DocumentNumbers;
using QuroErp.Application.Tests.Common.DocumentNumbers;
using QuroErp.Application.Mm.Inventory;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Inventory;

namespace QuroErp.Application.Tests.Mm.Inventory;

/// <summary>
/// InventoryService with in-memory repositories: on-hand per item + warehouse, ledger, cost and sets
/// (MM-BR-001~008, MM-BR-601~603, MM-BR-606), Locations and lots (MM-BR-352~365, MM-BR-713, MM-BR-714).
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
        _inventory.AddItem(90, "SET", type: "KIT");
        _inventory.AddItem(1, "P1", onHand: (Main, 20m), cost: 3);
        _inventory.AddItem(2, "P2", onHand: (Main, 20m), cost: 5);
        _sets.Components.AddRange([new SetComponent(90, 1, 2), new SetComponent(90, 2, 1)]);

        var posted = await Post(new StockMovementRequest("DN", Day, 90, Main, -3, RefKey: "DN-1", RefSeq: 1));

        posted.Select(m => (m.ItemId, m.Qty, m.Amount)).Should().Equal((1L, -6m, -18m), (2L, -3m, -15m));
        _inventory.OnHand[(1, Main)].Should().Be(14);
        _inventory.OnHand[(2, Main)].Should().Be(17);
        _inventory.OnHand.Should().NotContainKey((90, Main), "a set keeps no stock");
    }

    // SD-BR-513, MM-BR-008: a Set is a stock item, so its shipment issues its own stock even when it has components
    [Fact, Trait("Id", "SD-UT-513"), Trait("Id", "MM-UT-008")]
    public async Task PostAsync_SetItemIssue_ReducesTheSetsOwnStock_NotItsComponents()
    {
        _inventory.AddItem(91, "ASM", type: "SET", onHand: (Main, 5m), cost: 20);
        _inventory.AddItem(1, "P1", onHand: (Main, 20m), cost: 3);
        _sets.Components.Add(new SetComponent(91, 1, 2));

        var posted = await Post(new StockMovementRequest("DN", Day, 91, Main, -2, RefKey: "DN-1", RefSeq: 1));

        posted.Select(m => (m.ItemId, m.Qty, m.Amount, m.SetItemId)).Should().Equal((91L, -2m, -40m, (long?)null));
        _inventory.OnHand[(91, Main)].Should().Be(3);
        _inventory.OnHand[(1, Main)].Should().Be(20);
    }

    [Fact, Trait("Id", "CORE-011-T01")]
    public async Task PostAsync_SetLine_LocksItemSetAndComponentsInOnePassAndRemembersTheSet()
    {
        _inventory.AddItem(90, "SET", type: "KIT");
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
        _inventory.AddItem(90, "SET", type: "KIT");

        var act = () => Post(new StockMovementRequest("DN", Day, 90, Main, -1));

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be("SD-E-509");
        error.Args.Should().Equal("SET");
        _inventory.Movements.Should().BeEmpty();
    }

    [Fact, Trait("Id", "CORE-011-T02")]
    public async Task PostAsync_ComponentsChangedBeforeTheLock_ThrowsConcurrency()
    {
        _inventory.AddItem(90, "SET", type: "KIT");
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
        _inventory.AddItem(90, "SET", type: "KIT");
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
        _inventory.AddItem(90, "SET", type: "KIT");
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

    // Lot-managed item 20 "L" since 10/1 (MM-BR-351~356, MM-BR-363, MM-BR-365)
    private static readonly DateOnly LotStart = new(2026, 10, 1);

    private void AddLotItem(decimal onStar = 0)
    {
        _inventory.AddItem(20, "L", cost: 2, lotManagedFrom: LotStart);
        if (onStar != 0)
        {
            _inventory.AddKeyStock(20, Main, "*", "*", onStar);
        }
    }

    [Fact, Trait("Id", "MM-UT-353"), Trait("Rule", "MM-BR-353")]
    public async Task PostAsync_PurchaseReceiptWithoutLot_GetsTheNextAutomaticNumberAndItsReceivedDate()
    {
        AddLotItem();

        await Post(new StockMovementRequest("GR", Day, 20, Main, 5, Price: 2));
        var posted = await Post(new StockMovementRequest("GR", Day, 20, Main, 3, Price: 2), new StockMovementRequest("GR", Day, 20, Main, 1, Price: 2));

        _inventory.LotNumbers(20).Should().Equal("*", "261010-001", "261010-002", "261010-003");
        posted.Select(m => _inventory.LotNo(m.LotId)).Should().Equal("261010-002", "261010-003");
        _inventory.KeyQty(20, Main, "*", "261010-001").Should().Be(5);
        _inventory.KeyQty(20, Main, "*", "261010-003").Should().Be(1);
        _inventory.ReceivedDates.Values.Should().AllBeEquivalentTo(Day);
        _inventory.OnHand[(20, Main)].Should().Be(9);
    }

    [Fact, Trait("Id", "MM-UT-353"), Trait("Rule", "MM-BR-353")]
    public async Task PostAsync_LotIncreaseWithAnExistingNumber_AddsToThatLot()
    {
        AddLotItem();
        await Post(new StockMovementRequest("GR", Day, 20, Main, 5, Price: 2, LotNo: "B-1"));

        await Post(new StockMovementRequest("GR", Day, 20, Main, 2, Price: 2, LotNo: " b-1 "));

        _inventory.LotNumbers(20).Should().Equal("*", "B-1");
        _inventory.KeyQty(20, Main, "*", "B-1").Should().Be(7);
    }

    [Theory, Trait("Id", "MM-UT-352"), Trait("Rule", "MM-BR-352")]
    [InlineData("A B")]
    [InlineData("1234567890123456789012345678901")]
    public async Task PostAsync_LotNumberAgainstTheRules_IsMmE352(string lotNo)
    {
        AddLotItem();

        var act = () => Post(new StockMovementRequest("GR", Day, 20, Main, 5, Price: 2, LotNo: lotNo));

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("MM-E-352");
        _inventory.LotNumbers(20).Should().Equal("*");
    }

    [Fact, Trait("Id", "MM-UT-365"), Trait("Rule", "MM-BR-365")]
    public async Task PostAsync_ReceiptsOtherThanPurchase_AndDecreasesWithoutALot_UseLotStar()
    {
        AddLotItem(onStar: 10);

        var posted = await Post(
            new StockMovementRequest("UR", Day, 20, Main, 4, Price: 2),
            new StockMovementRequest("DN", Day, 20, Main, -3));

        posted.Select(m => _inventory.LotNo(m.LotId)).Should().Equal("*", "*");
        _inventory.LotNumbers(20).Should().Equal("*");
        _inventory.KeyQty(20, Main, "*", "*").Should().Be(11);
    }

    [Fact, Trait("Id", "MM-UT-365"), Trait("Rule", "MM-BR-365")]
    public async Task PostAsync_AReceiptOtherThanPurchase_NamesAnExistingLot_ButNeverMakesOne()
    {
        AddLotItem();
        await Post(new StockMovementRequest("GR", Day, 20, Main, 5, Price: 2, LotNo: "B-1"));

        var back = await Post(new StockMovementRequest("RT", Day, 20, Main, 1, Price: 2, LotNo: "b-1"));
        var act = () => Post(new StockMovementRequest("UR", Day, 20, Main, 1, Price: 2, LotNo: "NEW"));

        _inventory.LotNo(back.Single().LotId).Should().Be("B-1");
        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be("MM-E-352");
        error.Args.Should().Equal("L", "NEW");
        _inventory.LotNumbers(20).Should().Equal("*", "B-1");
    }

    [Fact, Trait("Id", "MM-UT-355"), Trait("Rule", "MM-BR-355")]
    public async Task PostAsync_DecreaseFromALotTheItemDoesNotHave_IsMmE355AndSavesNothing()
    {
        AddLotItem(onStar: 10);

        var act = () => Post(new StockMovementRequest("DN", Day, 20, Main, -1, LotNo: "NONE"));

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be("MM-E-355");
        error.Args.Should().Equal("L");
        _inventory.Movements.Should().BeEmpty();
    }

    [Fact, Trait("Id", "MM-UT-363"), Trait("Rule", "MM-BR-363")]
    public async Task PostAsync_ItemWithoutLotManagement_TakesOnlyLotStar()
    {
        _inventory.AddItem(10, "A", onHand: (Main, 5m), cost: 2);

        var star = await Post(new StockMovementRequest("DN", Day, 10, Main, -1, LotNo: "*"));
        var act = () => Post(new StockMovementRequest("DN", Day, 10, Main, -1, LotNo: "X"));

        _inventory.LotNo(star.Single().LotId).Should().Be("*");
        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be("MM-E-363");
        error.Args.Should().Equal("A", "X");
        _inventory.LotNumbers(10).Should().Equal("*");
    }

    [Fact, Trait("Id", "MM-UT-356"), Trait("Rule", "MM-BR-356")]
    public async Task PostAsync_LotBalanceShort_IsBlockedEvenWhenNegativeStockIsAllowedInTheMonth()
    {
        _settings.Mode = "ALLOW_IN_MONTH";
        AddLotItem(onStar: 10);
        await Post(new StockMovementRequest("GR", Day, 20, Main, 5, Price: 2, LotNo: "B"));

        // The warehouse has 15, lot B only 5
        var act = () => Post(new StockMovementRequest("DN", Day, 20, Main, -6, LotNo: "B"));

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be("MM-E-356");
        error.Args.Should().Equal("L", "WH1", "*", "B", "2026-10-10", -1m);
        _inventory.KeyQty(20, Main, "*", "B").Should().Be(5);
    }

    [Fact, Trait("Id", "MM-UT-356"), Trait("Rule", "MM-BR-356")]
    public async Task PostAsync_LotIssueDatedBeforeTheLotWasReceived_IsBlockedAtThatDate()
    {
        AddLotItem(onStar: 10);
        await Post(new StockMovementRequest("GR", new DateOnly(2026, 10, 20), 20, Main, 5, Price: 2, LotNo: "B"));

        var act = () => Post(new StockMovementRequest("DN", new DateOnly(2026, 10, 15), 20, Main, -2, LotNo: "B"));

        (await act.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal("L", "WH1", "*", "B", "2026-10-15", -2m);
    }

    [Fact, Trait("Id", "MM-UT-356"), Trait("Rule", "MM-BR-356")]
    public async Task PostAsync_LotStar_MovesBeforeLotManagementStarted_OtherLotsDoNot()
    {
        AddLotItem(onStar: 10);

        var star = await Post(new StockMovementRequest("DN", LotStart.AddDays(-5), 20, Main, -4));
        var before = () => Post(new StockMovementRequest("GR", LotStart.AddDays(-1), 20, Main, 1, Price: 2, LotNo: "B"));

        _inventory.KeyQty(20, Main, "*", "*").Should().Be(6);
        star.Single().LotId.Should().NotBeNull();
        (await before.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal("L", "WH1", "*", "B", "2026-09-30", 0m);
    }

    [Fact, Trait("Id", "MM-UT-355"), Trait("Rule", "MM-BR-355")]
    public async Task ReverseAsync_PutsTheStockBackOnTheOriginalLocationAndLot()
    {
        AddLotItem();
        _inventory.SetLocationManaged(20);
        var shelf = _inventory.AddLocation(Main, "A01");
        var receipt = (await Post(new StockMovementRequest("GR", Day, 20, Main, 5, Price: 2, LotNo: "B", LocationId: shelf.Id))).Single();

        var reversal = (await Service.ReverseAsync([receipt.Id], Day, "kim", null!, CancellationToken.None)).Single();

        (reversal.LocationId, reversal.LotId).Should().Be((shelf.Id, receipt.LotId));
        _inventory.KeyQty(20, Main, "A01", "B").Should().Be(0);
        _inventory.OnHand[(20, Main)].Should().Be(0);
    }

    [Fact, Trait("Id", "MM-UT-714"), Trait("Rule", "MM-BR-714")]
    public async Task PostAsync_LocksTheStarRowsFirst_ThenEveryKeyInItemWarehouseLocationLotOrder()
    {
        AddLotItem(onStar: 10);
        _inventory.AddItem(10, "A", onHand: (Main, 5m), cost: 2);
        var star10 = _inventory.StarLot(10);
        var star20 = _inventory.StarLot(20);
        var location = _inventory.StarLocation(Main).Id;

        await Post(
            new StockMovementRequest("DN", Day, 20, Main, -1),
            new StockMovementRequest("DN", Day, 10, Main, -1));

        _inventory.LockLog.Should().Equal(
            "onhand 10/1", "onhand 20/1", $"key 10/1/{location}/{star10}", $"key 20/1/{location}/{star20}");
    }

    [Fact, Trait("Id", "MM-UT-713"), Trait("Rule", "MM-BR-713")]
    public async Task PostAsync_LocationOtherThanStar_OnlyForALocationManagedItem_InUse_InTheLineWarehouse()
    {
        _inventory.AddItem(10, "A", onHand: (Main, 5m), cost: 2);
        var shelf = _inventory.AddLocation(Main, "A01");
        var otherWarehouse = _inventory.AddLocation(Second, "B01");
        var closed = _inventory.AddLocation(Main, "Z99", inUse: false);

        var notManaged = () => Post(new StockMovementRequest("UR", Day, 10, Main, 1, Price: 2, LocationId: shelf.Id));
        (await notManaged.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal("A", "WH1", "A01");

        _inventory.SetLocationManaged(10);
        var wrongWarehouse = () => Post(new StockMovementRequest("UR", Day, 10, Main, 1, Price: 2, LocationId: otherWarehouse.Id));
        var notInUse = () => Post(new StockMovementRequest("UR", Day, 10, Main, 1, Price: 2, LocationId: closed.Id));
        var missing = () => Post(new StockMovementRequest("UR", Day, 10, Main, 1, Price: 2, LocationId: 999));
        (await wrongWarehouse.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("MM-E-713");
        (await notInUse.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal("A", "WH1", "Z99");
        (await missing.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal("A", "WH1", "999");
        _inventory.Movements.Should().BeEmpty();

        var posted = await Post(new StockMovementRequest("UR", Day, 10, Main, 2, Price: 2, LocationId: shelf.Id));

        posted.Single().LocationId.Should().Be(shelf.Id);
        _inventory.KeyQty(10, Main, "A01", "*").Should().Be(2);
        _inventory.OnHand[(10, Main)].Should().Be(7);
    }

    [Fact, Trait("Id", "MM-UT-714"), Trait("Rule", "MM-BR-714")]
    public async Task PostAsync_LocationBalanceShort_IsBlockedEvenWhenNegativeStockIsAllowedInTheMonth()
    {
        _settings.Mode = "ALLOW_IN_MONTH";
        _inventory.AddItem(10, "A", onHand: (Main, 10m), cost: 2);
        _inventory.SetLocationManaged(10);
        var shelf = _inventory.AddLocation(Main, "A01");
        await Post(new StockMovementRequest("UR", Day, 10, Main, 2, Price: 2, LocationId: shelf.Id));

        var act = () => Post(new StockMovementRequest("DN", Day, 10, Main, -3, LocationId: shelf.Id));

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be("MM-E-714");
        error.Args.Should().Equal("A", "WH1", "A01", "*", -1m);
    }

    [Theory, Trait("Id", "MM-UT-714"), Trait("Rule", "MM-BR-714")]
    [InlineData("DENY", true)]
    [InlineData("ALLOW_IN_MONTH", false)]
    public async Task PostAsync_StarRowBelowZero_FollowsTheNegativeStockMode(string mode, bool blocked)
    {
        // The warehouse has 10, all of it on lot B: nothing is left on ('*', '*')
        _settings.Mode = mode;
        AddLotItem();
        await Post(new StockMovementRequest("GR", Day, 20, Main, 10, Price: 2, LotNo: "B"));

        var act = () => Post(new StockMovementRequest("DN", Day, 20, Main, -1));

        if (blocked)
        {
            var error = (await act.Should().ThrowAsync<DomainException>()).Which;
            error.Code.Should().Be("MM-E-714");
            error.Args.Should().Equal("L", "WH1", "*", "*", -1m);
        }
        else
        {
            await act.Should().NotThrowAsync();
            _inventory.KeyQty(20, Main, "*", "*").Should().Be(-1);
        }
    }

    // Kit 90 "SET" = 2 × lot item L (20) + 1 × P1 (1, no Lot or Location management). L has 8 in the main warehouse: 1 on
    // Lot '*', 2 on B1 (received 9/1) on shelf A01 and 5 on A1 (received 9/5) (SD-BR-518)
    private void AddKitWithLotComponent()
    {
        AddLotItem(onStar: 1);
        _inventory.SetLocationManaged(20);
        _inventory.AddLocation(Main, "A01");
        _inventory.AddKeyStock(20, Main, "A01", "B1", 2);
        _inventory.AddKeyStock(20, Main, "*", "A1", 5);
        _inventory.SetReceived(20, "B1", new DateOnly(2026, 9, 1));
        _inventory.SetReceived(20, "A1", new DateOnly(2026, 9, 5));
        _inventory.AddItem(1, "P1", onHand: (Main, 20m), cost: 3);
        _inventory.AddItem(90, "SET", type: "KIT");
        _sets.Components.AddRange([new SetComponent(90, 20, 2), new SetComponent(90, 1, 1)]);
    }

    [Fact, Trait("Id", "SD-UT-518"), Trait("Rule", "SD-BR-518"), Trait("Rule", "MM-BR-007")]
    public async Task PostAsync_KitIssue_TakesTheLotComponentInFifoOrder_SplitOverItsLocationsAndLots()
    {
        AddKitWithLotComponent();

        var posted = await Post(new StockMovementRequest("DN", Day, 90, Main, -3, RefKey: "DN-1", RefSeq: 1));

        posted.Select(m => (m.ItemId, _inventory.LotNo(m.LotId), m.Qty, m.SetItemId, m.RefKey)).Should().Equal(
            (20L, "*", -1m, 90L, "DN-1"), (20L, "B1", -2m, 90L, "DN-1"), (20L, "A1", -3m, 90L, "DN-1"), (1L, "*", -3m, 90L, "DN-1"));
        posted[1].LocationId.Should().Be(_inventory.Locations.Single(l => l.LocationCode == "A01").Id);
        (_inventory.KeyQty(20, Main, "*", "*"), _inventory.KeyQty(20, Main, "A01", "B1"), _inventory.KeyQty(20, Main, "*", "A1"))
            .Should().Be((0m, 0m, 2m));
        _inventory.BalanceReads.Should().Equal([(20L, Main, Day)], "only the Lot-managed component is allocated");
    }

    [Fact, Trait("Id", "SD-UT-518"), Trait("Rule", "SD-BR-518")]
    public async Task PostAsync_TwoKitLinesInOneDocument_ShareTheBalances()
    {
        AddKitWithLotComponent();

        var posted = await Post(
            new StockMovementRequest("DN", Day, 90, Main, -2, RefSeq: 1),
            new StockMovementRequest("DN", Day, 90, Main, -1, RefSeq: 2));

        posted.Where(m => m.ItemId == 20).Select(m => (m.RefSeq, _inventory.LotNo(m.LotId), m.Qty)).Should().Equal(
            (1, "*", -1m), (1, "B1", -2m), (1, "A1", -1m), (2, "A1", -2m));
        _inventory.KeyQty(20, Main, "*", "A1").Should().Be(2);
    }

    [Theory, Trait("Id", "SD-UT-518"), Trait("Rule", "SD-BR-518")]
    [InlineData("DENY")]
    [InlineData("ALLOW_IN_MONTH")]
    public async Task PostAsync_KitIssue_LotComponentShort_ThrowsSdE518WhateverTheNegativeStockMode(string mode)
    {
        _settings.Mode = mode;
        AddKitWithLotComponent();

        var act = () => Post(new StockMovementRequest("DN", Day, 90, Main, -5));

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be(SetComponent.NotEnoughStock);
        error.Args.Should().Equal("SET", "L", "WH1", 10m, 8m);
        _inventory.Movements.Should().BeEmpty();
    }

    [Fact, Trait("Id", "SD-UT-518"), Trait("Rule", "SD-BR-518"), Trait("Rule", "MM-BR-365")]
    public async Task PostAsync_KitReturn_PutsTheComponentsOnLotStar_AndTheNextIssueTakesThatFirst()
    {
        AddKitWithLotComponent();

        var returned = await Post(new StockMovementRequest("RT", Day, 90, Main, 1));
        var issued = await Post(new StockMovementRequest("DN", Day, 90, Main, -2));

        returned.Select(m => (m.ItemId, _inventory.LotNo(m.LotId), m.Qty)).Should().Equal((20L, "*", 2m), (1L, "*", 1m));
        issued.Select(m => (m.ItemId, _inventory.LotNo(m.LotId), m.Qty)).Should().Equal((20L, "*", -3m), (20L, "B1", -1m), (1L, "*", -2m));
        _inventory.KeyQty(1, Main, "*", "*").Should().Be(19, "P1 has no Lot or Location management, so it stays on ('*', '*')");
    }

    private sealed class FakeInventoryRepository : IInventoryRepository
    {
        public Task<IReadOnlyDictionary<long, decimal>> GetSellableOnHandAsync(
            IReadOnlyCollection<long> itemIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public string? CloseYearMonth { get; set; }

        public Dictionary<long, ItemStock> Items { get; } = [];

        /// <summary>mm_onhand: one balance per item + warehouse + Location + Lot.</summary>
        public Dictionary<(long ItemId, long WarehouseId, long LocationId, long LotId), decimal> KeyOnHand { get; } = [];

        /// <summary>Item + warehouse balances: the sum of their keys.</summary>
        public Dictionary<(long ItemId, long WarehouseId), decimal> OnHand =>
            KeyOnHand.GroupBy(e => (e.Key.ItemId, e.Key.WarehouseId)).ToDictionary(g => g.Key, g => g.Sum(e => e.Value));

        public List<GoodsMovement> Movements { get; } = [];

        public List<(long ItemId, long WarehouseId)> Locked { get; } = [];

        public List<long[]> LockedItemCalls { get; } = [];

        /// <summary>Starting stock, read as the closing balance of a closed month (keeps ledger = on-hand, MM-BR-605).</summary>
        public Dictionary<(long ItemId, long WarehouseId), decimal> Opening { get; } = [];

        public List<StockLocation> Locations { get; } = [];

        public List<LotRef> Lots { get; } = [];

        public Dictionary<long, DateOnly> ReceivedDates { get; } = [];

        public List<string> LockLog { get; } = [];

        private readonly Dictionary<long, long> _lotItems = [];

        public void AddItem(
            long id, string itemCd, (long WarehouseId, decimal Qty)? onHand = null, decimal cost = 0, string type = "INVENTORY",
            DateOnly? lotManagedFrom = null)
        {
            Items[id] = new ItemStock(id, itemCd, type, 0, 0, cost, lotManagedFrom is not null, lotManagedFrom);
            Ensure(id, "*");
            if (onHand is { } o)
            {
                AddOnHand(id, o.WarehouseId, o.Qty);
            }
        }

        /// <summary>Stock on ('*', '*') read as the closing balance of a closed month.</summary>
        public void AddOnHand(long itemId, long warehouseId, decimal qty) => AddKeyStock(itemId, warehouseId, "*", "*", qty);

        public void AddKeyStock(long itemId, long warehouseId, string locationCode, string lotNo, decimal qty)
        {
            var key = (itemId, warehouseId, Location(warehouseId, locationCode).Id, Ensure(itemId, lotNo).Id);
            KeyOnHand[key] = KeyOnHand.GetValueOrDefault(key) + qty;
            Opening[(itemId, warehouseId)] = Opening.GetValueOrDefault((itemId, warehouseId)) + qty;
            var item = Items[itemId];
            Items[itemId] = Copy(item, item.OnHandQty + qty, item.StockValue + qty * item.MovingAverageCost, item.LocationManaged);
        }

        public void SetLocationManaged(long itemId) => Items[itemId] = Copy(Items[itemId], locationManaged: true);

        public void SetReceived(long itemId, string lotNo, DateOnly receivedDt) => ReceivedDates[Ensure(itemId, lotNo).Id] = receivedDt;

        public List<(long ItemId, long WarehouseId, DateOnly AsOf)> BalanceReads { get; } = [];

        // Same result as the SQL in StockBalanceSql: current balance minus later movements, above zero, Locations in use
        public Task<IReadOnlyList<StockBalance>> FindKeyBalancesAsync(
            long itemId, long warehouseId, DateOnly asOf, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            BalanceReads.Add((itemId, warehouseId, asOf));
            return Task.FromResult<IReadOnlyList<StockBalance>>(KeyOnHand
                .Where(e => e.Key.ItemId == itemId && e.Key.WarehouseId == warehouseId)
                .Select(e =>
                {
                    var location = Locations.Single(l => l.Id == e.Key.LocationId);
                    var lot = Lots.Single(l => l.Id == e.Key.LotId);
                    var later = Movements.Where(m => m.ItemId == itemId && m.WarehouseId == warehouseId && m.LocationId == location.Id
                        && m.LotId == lot.Id && m.DocumentDt > asOf).Sum(m => m.Qty);
                    return (location.InUse, Balance: new StockBalance(
                        location.Id, location.LocationCode, lot.Id, lot.LotNo, ReceivedDates.TryGetValue(lot.Id, out var d) ? d : null, e.Value - later));
                })
                .Where(b => b.InUse && b.Balance.Qty > 0).Select(b => b.Balance).ToList());
        }

        public StockLocation AddLocation(long warehouseId, string code, bool inUse = true)
        {
            var location = new StockLocation(Locations.Count + 1, warehouseId, code, inUse);
            Locations.Add(location);
            return location;
        }

        public StockLocation StarLocation(long warehouseId) => Location(warehouseId, "*");

        public long StarLot(long itemId) => Ensure(itemId, "*").Id;

        public decimal KeyQty(long itemId, long warehouseId, string locationCode, string lotNo) =>
            KeyOnHand.GetValueOrDefault((itemId, warehouseId, Location(warehouseId, locationCode).Id, Ensure(itemId, lotNo).Id));

        public IEnumerable<string> LotNumbers(long itemId) => Lots.Where(l => _lotItems[l.Id] == itemId).Select(l => l.LotNo);

        public string? LotNo(long? lotId) => Lots.FirstOrDefault(l => l.Id == lotId)?.LotNo;

        private StockLocation Location(long warehouseId, string code) =>
            Locations.FirstOrDefault(l => l.WarehouseId == warehouseId && l.LocationCode == code) ?? AddLocation(warehouseId, code);

        private static ItemStock Copy(ItemStock i, decimal? onHandQty = null, decimal? stockValue = null, bool? locationManaged = null) =>
            new(i.ItemId, i.ItemCd, i.Type, onHandQty ?? i.OnHandQty, stockValue ?? i.StockValue, i.MovingAverageCost, i.LotManaged,
                i.LotManagedFrom, locationManaged ?? i.LocationManaged);

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

        public Task<LotRef?> FindLotAsync(long itemId, long? lotId, string? lotNo, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(Lots.FirstOrDefault(l => _lotItems[l.Id] == itemId && (lotId is { } id ? l.Id == id : l.LotNo == lotNo)));

        public Task<LotRef> EnsureLotAsync(long itemId, string lotNo, DateOnly receivedDt, string user, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            var isNew = !LotNumbers(itemId).Contains(lotNo);
            var lot = Ensure(itemId, lotNo);
            if (isNew)
            {
                ReceivedDates[lot.Id] = receivedDt;
            }
            return Task.FromResult(lot);
        }

        public Task<int> GetLastAutoLotSeqAsync(long itemId, string prefix, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(Lots.Where(l => _lotItems[l.Id] == itemId && l.LotNo.StartsWith(prefix, StringComparison.Ordinal)
                    && int.TryParse(l.LotNo[prefix.Length..], out _))
                .Select(l => int.Parse(l.LotNo[prefix.Length..], System.Globalization.CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Max());

        public Task<StockLocation?> FindLocationAsync(long warehouseId, long? locationId, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(locationId is { } id ? Locations.FirstOrDefault(l => l.Id == id) : StarLocation(warehouseId));

        public Task<decimal> LockKeyOnHandAsync(
            long itemId, long warehouseId, long locationId, long lotId, string user, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            LockLog.Add($"key {itemId}/{warehouseId}/{locationId}/{lotId}");
            return Task.FromResult(KeyOnHand.GetValueOrDefault((itemId, warehouseId, locationId, lotId)));
        }

        public Task AddOnHandAsync(
            long itemId, long warehouseId, long locationId, long lotId, decimal qty, string user, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            var key = (itemId, warehouseId, locationId, lotId);
            KeyOnHand[key] = KeyOnHand.GetValueOrDefault(key) + qty;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<StockKeyDailyQty>> GetKeyDailyQtyAsync(
            long itemId, long warehouseId, long locationId, long lotId, DateOnly fromDate, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<StockKeyDailyQty>>(Movements
                .Where(m => m.ItemId == itemId && m.WarehouseId == warehouseId && m.LocationId == locationId && m.LotId == lotId && m.DocumentDt >= fromDate)
                .GroupBy(m => m.DocumentDt).OrderBy(g => g.Key).Select(g => new StockKeyDailyQty(g.Key, g.Sum(m => m.Qty))).ToList());

        public Task<string?> GetCloseYearMonthAsync(IDbTransaction transaction, CancellationToken cancellationToken) => Task.FromResult(CloseYearMonth);

        // Copies, so a failed post does not change the stored items
        public Task<IReadOnlyList<ItemStock>> LockItemsAsync(IReadOnlyCollection<long> itemIds, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            LockedItemCalls.Add([.. itemIds]);
            return Task.FromResult<IReadOnlyList<ItemStock>>(itemIds.Where(Items.ContainsKey).Select(id => Copy(Items[id])).ToList());
        }

        public Task<OnHandRow> LockOnHandAsync(long itemId, long warehouseId, string user, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Locked.Add((itemId, warehouseId));
            LockLog.Add($"onhand {itemId}/{warehouseId}");
            StarLocation(warehouseId);
            Ensure(itemId, "*");
            return Task.FromResult(new OnHandRow(itemId, warehouseId, $"WH{warehouseId}", OnHand.GetValueOrDefault((itemId, warehouseId))));
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
