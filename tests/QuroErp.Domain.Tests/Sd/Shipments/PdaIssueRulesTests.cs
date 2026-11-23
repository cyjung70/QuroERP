using FluentAssertions;
using QuroErp.Domain.Common;
using QuroErp.Domain.Sd.Shipments;

namespace QuroErp.Domain.Tests.Sd.Shipments;

/// <summary>PDA shipment scans (SD-UC-054): the scanned order or invoice number and the items scanned on it (MM-BR-921, MM-BR-923).</summary>
public class PdaIssueRulesTests
{
    private const long Customer = 7;

    [Theory, Trait("Id", "SD-UC-054-T05"), Trait("Id", "MM-UT-921"), Trait("Rule", "MM-BR-921"), Trait("Id", "MM-UT-923"), Trait("Rule", "MM-BR-923")]
    [InlineData(false, "SO", "SHIP_AND_BILL", null, true, "MM-E-923")]
    [InlineData(true, "CL", "SHIP_AND_BILL", null, false, "SD-E-172")]
    [InlineData(true, "RE", "SHIP_AND_BILL", null, false, "MM-E-923")]
    [InlineData(true, "SO", "BILL_THEN_SHIP", null, true, "SD-E-174")]
    [InlineData(true, "SO", "SHIP_AND_BILL", 8L, true, "Common-E-006")]
    [InlineData(true, "SO", "SHIP_AND_BILL", null, true, null)]
    [InlineData(true, "RE", "SHIP_THEN_BILL", Customer, true, null)]
    [InlineData(true, null, "SHIP_AND_BILL", null, true, null)]
    public void PdaOrder_MustBeKnown_ShippableByOrder_AndHaveALineLeft(
        bool found, string? status, string flow, long? customerId, bool hasOpenLine, string? expected)
    {
        var act = () => PdaIssueRules.EnsurePdaOrder("SO-1", found, status, flow, Customer, customerId, hasOpenLine);

        if (expected is null)
        {
            act.Should().NotThrow();
        }
        else
        {
            var error = act.Should().Throw<DomainException>().Which;
            (error.Code, error.Args).Should().BeEquivalentTo((expected, new object[] { "SO-1" }));
        }
    }

    [Theory, Trait("Id", "SD-UC-054-T05"), Trait("Id", "MM-UT-921"), Trait("Rule", "MM-BR-921"), Trait("Id", "MM-UT-923"), Trait("Rule", "MM-BR-923")]
    [InlineData(false, "OP", "BILL_THEN_SHIP", null, true, "MM-E-923")]
    [InlineData(true, "VO", "BILL_THEN_SHIP", null, true, "SD-E-175")]
    [InlineData(true, "OP", "SHIP_AND_BILL", null, true, "SD-E-175")]
    [InlineData(true, "PA", "BILL_THEN_SHIP", 8L, true, "Common-E-006")]
    [InlineData(true, "CL", "BILL_THEN_SHIP", null, false, "MM-E-923")]
    [InlineData(true, "CL", "BILL_THEN_SHIP", Customer, true, null)]
    public void PdaInvoice_MustBeAKnownBillThenShipInvoice_WithALineLeft(
        bool found, string? status, string flow, long? customerId, bool hasOpenLine, string? expected)
    {
        var act = () => PdaIssueRules.EnsurePdaInvoice("DN-1", found, status, flow, Customer, customerId, hasOpenLine);

        if (expected is null)
        {
            act.Should().NotThrow();
        }
        else
        {
            var error = act.Should().Throw<DomainException>().Which;
            error.Code.Should().Be(expected);
            error.Args[0].Should().Be("DN-1");
        }
    }

    [Fact, Trait("Id", "SD-UC-054-T04"), Trait("Id", "MM-UT-923"), Trait("Rule", "MM-BR-923")]
    public void PdaItem_MustHaveALineLeftOnTheScannedDocument()
    {
        var onOrder = () => PdaIssueRules.EnsureItemOnDocument(2, "IT-2", [1, 2]);
        var notOnOrder = () => PdaIssueRules.EnsureItemOnDocument(3, "SV-3", [1, 2]);
        var unknown = () => PdaIssueRules.EnsureItemOnDocument(99, null, [1]);

        onOrder.Should().NotThrow();
        notOnOrder.Should().Throw<DomainException>().Which.Args.Should().Equal("SV-3");
        var error = unknown.Should().Throw<DomainException>().Which;
        (error.Code, error.Args).Should().BeEquivalentTo(("MM-E-923", new object[] { "99" }));
    }
}
