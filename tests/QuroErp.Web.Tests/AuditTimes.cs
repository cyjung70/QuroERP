using QuroErp.Web.Common;

namespace QuroErp.Web.Tests;

/// <summary>Created and updated times of the stored records in the screen fakes (TASK-CORE-050).</summary>
public static class AuditTimes
{
    public static readonly DateTimeOffset Created = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    public static readonly DateTimeOffset Updated = new(2026, 10, 28, 9, 30, 0, TimeSpan.Zero);

    /// <summary>The two audit lines for a record created by <paramref name="createdBy"/> and changed by <paramref name="updatedBy"/>, in Seoul time.</summary>
    public static string[] Shown(string createdBy, string updatedBy)
    {
        var seoul = CompanyTime.Find("Asia/Seoul");
        return [$"{CompanyTime.Format(Created, seoul)} {createdBy}", $"{CompanyTime.Format(Updated, seoul)} {updatedBy}"];
    }
}
