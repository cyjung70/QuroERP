using QuroErp.Domain.Common;

namespace QuroErp.Domain.Mm.PurchaseOrders;

/// <summary>
/// Which actions a purchase order allows in its status (purchase-order.md "상태와 버튼"). The screen disables
/// buttons with the Can* methods; the API calls the Ensure* methods, which return 422 with the same rules.
/// </summary>
public static class PoStatusRules
{
    /// <summary>Edit or delete after any receipt or in a status other than DR/PO. Arg: PO number.</summary>
    public const string NotEditableCode = "MM-E-013";

    /// <summary>Status transition not allowed. Args: from status code, to status code.</summary>
    public const string InvalidTransitionCode = "MM-E-019";

    /// <summary>Approve a PO that is not a saved DR or has no lines. Arg: PO number.</summary>
    public const string NotApprovableCode = "MM-E-020";

    /// <summary>Close a PO that is not PO/RE. Arg: PO number.</summary>
    public const string NotClosableCode = "MM-E-022";

    /// <summary>Cancel a PO that is not PO or has a receipt. Arg: PO number.</summary>
    public const string NotCancellableCode = "MM-E-023";

    /// <summary>MM-BR-013: DR or PO and nothing received, whatever the status says.</summary>
    public static bool IsEditable(PurchaseOrder po) => po.Status is PoStatus.Draft or PoStatus.Approved && !po.HasReceipt;

    /// <summary>A new PO is saved as DR (MM-BR-019); a saved one follows MM-BR-013.</summary>
    public static bool CanSave(PurchaseOrder po) => po.PoNo is null || IsEditable(po);

    public static bool CanEditRows(PurchaseOrder po) => CanSave(po);

    /// <summary>A line with received quantity stays locked even while the header is editable (normally unreachable).</summary>
    public static bool IsLineEditable(PurchaseOrder po, PoLine line) => CanSave(po) && line.ReceivedQty == 0;

    public static bool CanDelete(PurchaseOrder po) => po.PoNo is not null && IsEditable(po);

    /// <summary>MM-BR-020: a saved DR with at least one line.</summary>
    public static bool CanApprove(PurchaseOrder po) => po.PoNo is not null && po.Status == PoStatus.Draft && po.ActiveLines.Any();

    /// <summary>MM-BR-022: PO or RE.</summary>
    public static bool CanClose(PurchaseOrder po) => po.PoNo is not null && po.Status is PoStatus.Approved or PoStatus.PartiallyReceived;

    /// <summary>MM-BR-023: PO with nothing received. A DR is deleted, not cancelled.</summary>
    public static bool CanCancel(PurchaseOrder po) => po.PoNo is not null && po.Status == PoStatus.Approved && !po.HasReceipt;

    public static bool CanPrint(PurchaseOrder po) => po.PoNo is not null;

    /// <summary>
    /// MM-BR-019: DR → PO (approve), PO/RE/CL among each other (receipt recalculation, MM-BR-018), PO/RE → MC (close),
    /// PO → CN (cancel). MC and CN are final; CL only changes by receipt recalculation.
    /// </summary>
    public static bool IsAllowedTransition(PoStatus from, PoStatus to) => (from, to) switch
    {
        (PoStatus.Draft, PoStatus.Approved) => true,
        (PoStatus.Approved or PoStatus.PartiallyReceived or PoStatus.Received,
            PoStatus.Approved or PoStatus.PartiallyReceived or PoStatus.Received) => from != to,
        (PoStatus.Approved or PoStatus.PartiallyReceived, PoStatus.Closed) => true,
        (PoStatus.Approved, PoStatus.Cancelled) => true,
        _ => false,
    };

    /// <exception cref="DomainException">MM-E-013.</exception>
    public static void EnsureEditable(PurchaseOrder po)
    {
        if (!IsEditable(po))
        {
            throw new DomainException(NotEditableCode, po.PoNo ?? "");
        }
    }

    /// <exception cref="DomainException">MM-E-019.</exception>
    public static void EnsureTransition(PoStatus from, PoStatus to)
    {
        if (!IsAllowedTransition(from, to))
        {
            throw new DomainException(InvalidTransitionCode, from.ToCode(), to.ToCode());
        }
    }

    /// <exception cref="DomainException">MM-E-020.</exception>
    public static void EnsureApprove(PurchaseOrder po)
    {
        if (!CanApprove(po))
        {
            throw new DomainException(NotApprovableCode, po.PoNo ?? "");
        }
    }

    /// <exception cref="DomainException">MM-E-022.</exception>
    public static void EnsureClose(PurchaseOrder po)
    {
        if (!CanClose(po))
        {
            throw new DomainException(NotClosableCode, po.PoNo ?? "");
        }
    }

    /// <exception cref="DomainException">MM-E-023.</exception>
    public static void EnsureCancel(PurchaseOrder po)
    {
        if (!CanCancel(po))
        {
            throw new DomainException(NotCancellableCode, po.PoNo ?? "");
        }
    }
}
