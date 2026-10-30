using FluentAssertions;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.PurchaseOrders;

namespace QuroErp.Domain.Tests.Mm.PurchaseOrders;

/// <summary>Status and button rules of a purchase order (MM-BR-013, MM-BR-019, MM-BR-020, MM-BR-022, MM-BR-023).</summary>
public class PoStatusRulesTests
{
    private const string PoNo = "PO261000012";

    private static PurchaseOrder Po(PoStatus status, decimal received = 0, string? poNo = PoNo, int lines = 1) =>
        new(poNo, status, Enumerable.Range(1, lines).Select(i => new PoLine(i, 10m, 100m, ReceivedQty: i == 1 ? received : 0)).ToList());

    [Theory, Trait("Id", "MM-UT-013"), Trait("Rule", "MM-BR-013")]
    [InlineData(PoStatus.Draft, true)]
    [InlineData(PoStatus.Approved, true)]
    [InlineData(PoStatus.PartiallyReceived, false)]
    [InlineData(PoStatus.Received, false)]
    [InlineData(PoStatus.Closed, false)]
    [InlineData(PoStatus.Cancelled, false)]
    public void IsEditable_ByStatus_OnlyDraftAndApproved(PoStatus status, bool expected)
    {
        PoStatusRules.IsEditable(Po(status)).Should().Be(expected);
    }

    [Fact, Trait("Id", "MM-UT-013"), Trait("Rule", "MM-BR-013")]
    public void Rules_AnyReceivedQty_LocksEditDeleteAndCancelEvenIfStatusLags()
    {
        var po = Po(PoStatus.Approved, received: 1);

        PoStatusRules.CanSave(po).Should().BeFalse();
        PoStatusRules.CanEditRows(po).Should().BeFalse();
        PoStatusRules.CanDelete(po).Should().BeFalse();
        PoStatusRules.CanCancel(po).Should().BeFalse();
        PoStatusRules.CanClose(po).Should().BeTrue();
    }

    [Fact, Trait("Id", "MM-UT-013"), Trait("Rule", "MM-BR-013")]
    public void EnsureEditable_ReceivedQty_ThrowsMmE013()
    {
        var act = () => PoStatusRules.EnsureEditable(Po(PoStatus.Draft, received: 2));

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be("MM-E-013");
        error.Args.Should().Equal(PoNo);
    }

    [Fact, Trait("Id", "MM-UT-013"), Trait("Rule", "MM-BR-013")]
    public void EnsureEditable_ApprovedWithoutReceipt_DoesNotThrow()
    {
        var act = () => PoStatusRules.EnsureEditable(Po(PoStatus.Approved));

        act.Should().NotThrow();
    }

    [Fact, Trait("Id", "MM-UT-013"), Trait("Rule", "MM-BR-013")]
    public void IsLineEditable_LineWithReceipt_IsLocked()
    {
        var po = new PurchaseOrder(PoNo, PoStatus.Draft, [new PoLine(1, 10m, 100m), new PoLine(2, 5m, 100m)]);

        PoStatusRules.IsLineEditable(po, po.Lines[0]).Should().BeTrue();
        PoStatusRules.IsLineEditable(po, po.Lines[0] with { ReceivedQty = 1 }).Should().BeFalse();
    }

    [Fact, Trait("Id", "MM-UT-020"), Trait("Rule", "MM-BR-020")]
    public void Rules_NewUnsavedPo_CanOnlyBeSaved()
    {
        var po = Po(PoStatus.Draft, poNo: null);

        PoStatusRules.CanSave(po).Should().BeTrue();
        PoStatusRules.CanApprove(po).Should().BeFalse();
        PoStatusRules.CanClose(po).Should().BeFalse();
        PoStatusRules.CanCancel(po).Should().BeFalse();
        PoStatusRules.CanDelete(po).Should().BeFalse();
        PoStatusRules.CanPrint(po).Should().BeFalse();
    }

    [Fact, Trait("Id", "MM-UT-020"), Trait("Rule", "MM-BR-020")]
    public void CanApprove_SavedDraftWithLine_IsTrue()
    {
        PoStatusRules.CanApprove(Po(PoStatus.Draft)).Should().BeTrue();
    }

    [Fact, Trait("Id", "MM-UC-003-T02"), Trait("Rule", "MM-BR-020")]
    public void EnsureApprove_NoLines_ThrowsMmE020()
    {
        var act = () => PoStatusRules.EnsureApprove(Po(PoStatus.Draft, lines: 0));

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MM-E-020");
    }

    [Fact, Trait("Id", "MM-UT-020"), Trait("Rule", "MM-BR-020")]
    public void EnsureApprove_OnlyLineMarkedForDelete_ThrowsMmE020()
    {
        var po = new PurchaseOrder(PoNo, PoStatus.Draft, [new PoLine(1, 10m, 100m, MarkedForDelete: true)]);

        var act = () => PoStatusRules.EnsureApprove(po);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MM-E-020");
    }

    [Fact, Trait("Id", "MM-UT-020"), Trait("Rule", "MM-BR-020")]
    public void EnsureApprove_AlreadyApproved_ThrowsMmE020()
    {
        var act = () => PoStatusRules.EnsureApprove(Po(PoStatus.Approved));

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MM-E-020");
    }

    [Theory, Trait("Id", "MM-UT-022"), Trait("Rule", "MM-BR-022")]
    [InlineData(PoStatus.Approved, true)]
    [InlineData(PoStatus.PartiallyReceived, true)]
    [InlineData(PoStatus.Draft, false)]
    [InlineData(PoStatus.Received, false)]
    [InlineData(PoStatus.Closed, false)]
    [InlineData(PoStatus.Cancelled, false)]
    public void CanClose_ByStatus_OnlyApprovedAndPartiallyReceived(PoStatus status, bool expected)
    {
        PoStatusRules.CanClose(Po(status)).Should().Be(expected);
    }

    [Fact, Trait("Id", "MM-UT-022"), Trait("Rule", "MM-BR-022")]
    public void EnsureClose_Draft_ThrowsMmE022()
    {
        var act = () => PoStatusRules.EnsureClose(Po(PoStatus.Draft));

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MM-E-022");
    }

    [Fact, Trait("Id", "MM-UT-023"), Trait("Rule", "MM-BR-023")]
    public void CanCancel_ApprovedWithoutReceipt_IsTrue()
    {
        PoStatusRules.CanCancel(Po(PoStatus.Approved)).Should().BeTrue();
    }

    [Theory, Trait("Id", "MM-UT-023"), Trait("Rule", "MM-BR-023")]
    [InlineData(PoStatus.Draft, 0)]
    [InlineData(PoStatus.Approved, 1)]
    [InlineData(PoStatus.PartiallyReceived, 1)]
    public void EnsureCancel_DraftOrReceived_ThrowsMmE023(PoStatus status, int received)
    {
        var act = () => PoStatusRules.EnsureCancel(Po(status, received: received));

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MM-E-023");
    }

    [Theory, Trait("Id", "MM-UT-019"), Trait("Rule", "MM-BR-019")]
    [InlineData(PoStatus.Draft, PoStatus.Approved)]
    [InlineData(PoStatus.Approved, PoStatus.PartiallyReceived)]
    [InlineData(PoStatus.Approved, PoStatus.Received)]
    [InlineData(PoStatus.PartiallyReceived, PoStatus.Approved)]
    [InlineData(PoStatus.PartiallyReceived, PoStatus.Received)]
    [InlineData(PoStatus.Received, PoStatus.PartiallyReceived)]
    [InlineData(PoStatus.Approved, PoStatus.Closed)]
    [InlineData(PoStatus.PartiallyReceived, PoStatus.Closed)]
    [InlineData(PoStatus.Approved, PoStatus.Cancelled)]
    public void EnsureTransition_Allowed_DoesNotThrow(PoStatus from, PoStatus to)
    {
        var act = () => PoStatusRules.EnsureTransition(from, to);

        act.Should().NotThrow();
    }

    [Theory, Trait("Id", "MM-UT-019"), Trait("Rule", "MM-BR-019")]
    [InlineData(PoStatus.Draft, PoStatus.Cancelled, "DR", "CN")]
    [InlineData(PoStatus.Draft, PoStatus.Closed, "DR", "MC")]
    [InlineData(PoStatus.Approved, PoStatus.Draft, "PO", "DR")]
    [InlineData(PoStatus.Received, PoStatus.Closed, "CL", "MC")]
    [InlineData(PoStatus.PartiallyReceived, PoStatus.Cancelled, "RE", "CN")]
    [InlineData(PoStatus.Closed, PoStatus.Approved, "MC", "PO")]
    [InlineData(PoStatus.Cancelled, PoStatus.Approved, "CN", "PO")]
    [InlineData(PoStatus.Approved, PoStatus.Approved, "PO", "PO")]
    public void EnsureTransition_NotAllowed_ThrowsMmE019WithCodes(PoStatus from, PoStatus to, string fromCode, string toCode)
    {
        var act = () => PoStatusRules.EnsureTransition(from, to);

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be("MM-E-019");
        error.Args.Should().Equal(fromCode, toCode);
    }

    [Theory, Trait("Id", "MM-UT-019"), Trait("Rule", "MM-BR-019")]
    [InlineData(PoStatus.Draft, "DR")]
    [InlineData(PoStatus.Approved, "PO")]
    [InlineData(PoStatus.PartiallyReceived, "RE")]
    [InlineData(PoStatus.Received, "CL")]
    [InlineData(PoStatus.Closed, "MC")]
    [InlineData(PoStatus.Cancelled, "CN")]
    public void PoStatusCode_RoundTrips(PoStatus status, string code)
    {
        status.ToCode().Should().Be(code);
        PoStatusCode.TryParse(code, out var parsed).Should().BeTrue();
        parsed.Should().Be(status);
    }

    [Theory, Trait("Id", "MM-UT-019"), Trait("Rule", "MM-BR-019")]
    [InlineData("AP")]
    [InlineData("dr")]
    [InlineData(null)]
    public void PoStatusCode_UnknownCode_IsNotParsed(string? code)
    {
        PoStatusCode.TryParse(code, out _).Should().BeFalse();
    }
}
