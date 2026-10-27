using FluentAssertions;
using QuroErp.Domain.Mm.Inventory;

namespace QuroErp.Domain.Tests.Mm.Inventory;

/// <summary>Moving average cost (MM-BR-005) and which items keep stock (MM-BR-008).</summary>
public class ItemStockTests
{
    [Fact, Trait("Id", "MM-UT-005")]
    public void Apply_ReceiptWithStock_CostIsValueOverQty()
    {
        var item = new ItemStock(1, "A", "INVENTORY", onHandQty: 10, stockValue: 100, movingAverageCost: 10);

        item.Apply(qty: 10, amount: 200, price: 20, recalculateCost: true);

        (item.OnHandQty, item.StockValue, item.MovingAverageCost).Should().Be((20m, 300m, 15m));
    }

    [Fact, Trait("Id", "MM-UT-005")]
    public void Apply_ReceiptFromNegativeToPositive_CostIsThisPriceAndValueIsQtyTimesCost()
    {
        var item = new ItemStock(1, "A", "INVENTORY", onHandQty: -2, stockValue: -20, movingAverageCost: 10);

        item.Apply(qty: 5, amount: 60, price: 12, recalculateCost: true);

        (item.OnHandQty, item.StockValue, item.MovingAverageCost).Should().Be((3m, 36m, 12m));
    }

    [Fact, Trait("Id", "MM-UT-005")]
    public void Apply_ReceiptLeavingStockAtOrBelowZero_KeepsCost()
    {
        var item = new ItemStock(1, "A", "INVENTORY", onHandQty: -5, stockValue: -50, movingAverageCost: 10);

        item.Apply(qty: 3, amount: 45, price: 15, recalculateCost: true);

        (item.OnHandQty, item.StockValue, item.MovingAverageCost).Should().Be((-2m, -5m, 10m));
    }

    [Fact, Trait("Id", "MM-UT-005")]
    public void Apply_ReceiptReversal_RecalculatesFromRemainingValue()
    {
        var item = new ItemStock(1, "A", "INVENTORY", onHandQty: 20, stockValue: 300, movingAverageCost: 15);

        item.Apply(qty: -10, amount: -200, price: 20, recalculateCost: true);

        (item.OnHandQty, item.StockValue, item.MovingAverageCost).Should().Be((10m, 100m, 10m));
    }

    [Fact, Trait("Id", "MM-UT-005")]
    public void Apply_Issue_ReducesQtyAndValueAndKeepsCost()
    {
        var item = new ItemStock(1, "A", "INVENTORY", onHandQty: 10, stockValue: 150, movingAverageCost: 15);

        item.Apply(qty: -4, amount: -60, price: 15, recalculateCost: false);

        (item.OnHandQty, item.StockValue, item.MovingAverageCost).Should().Be((6m, 90m, 15m));
    }

    [Theory, Trait("Id", "MM-UT-008")]
    [InlineData("INVENTORY", true, false)]
    [InlineData("SET", true, true)]
    [InlineData("SERVICE", false, false)]
    [InlineData(null, true, false)]
    public void IsStockManaged_EveryTypeButService(string? type, bool stockManaged, bool isSet)
    {
        var item = new ItemStock(1, "A", type, 0, 0, 0);

        item.IsStockManaged.Should().Be(stockManaged);
        item.IsSet.Should().Be(isSet);
    }
}
