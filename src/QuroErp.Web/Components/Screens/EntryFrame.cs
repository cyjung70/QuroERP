using QuroErp.Contracts.Common;

namespace QuroErp.Web.Components.Screens;

/// <summary>Kind of a status chip (entry-frame.md 6). The screen spec maps each status code to one.</summary>
public enum ChipKind
{
    /// <summary>No progress, e.g. draft or closed.</summary>
    Neutral,

    /// <summary>Confirmed or in progress.</summary>
    Brand,

    /// <summary>Partly done or needs attention.</summary>
    Warn,

    /// <summary>Done.</summary>
    Ok,

    /// <summary>Cancelled or void.</summary>
    Danger,
}

/// <summary>Audit columns shown at the bottom of the entry frame (created_*, updated_*).</summary>
public sealed record QuroAudit(string? CreatedBy, DateTimeOffset? CreatedAt, string? UpdatedBy, DateTimeOffset? UpdatedAt)
{
    /// <summary>The four audit fields of a one-record response (api.md); null when there is none yet.</summary>
    public static QuroAudit? From(AuditInfo? audit) =>
        audit is null ? null : new(audit.CreatedBy, audit.CreatedAt, audit.UpdatedBy, audit.UpdatedAt);
}
