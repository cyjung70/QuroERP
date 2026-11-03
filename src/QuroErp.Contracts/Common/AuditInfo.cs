namespace QuroErp.Contracts.Common;

/// <summary>
/// Audit fields of a one-record response that an S screen reads (GET by id, previous, next, one-row setting; api.md).
/// Times are UTC; users are the stored user IDs. The update request still sends only UpdatedAt for the concurrency check.
/// </summary>
public record AuditInfo(DateTimeOffset CreatedAt, string CreatedBy, DateTimeOffset UpdatedAt, string UpdatedBy);
