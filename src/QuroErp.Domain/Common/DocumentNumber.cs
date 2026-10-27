using System.Globalization;

namespace QuroErp.Domain.Common;

/// <summary>
/// Document numbers kept in sys_document_number: prefix + yyyyMM + a sequence that restarts every month.
/// </summary>
public static class DocumentNumber
{
    /// <param name="lastNo">The last number issued for this transaction type, or null.</param>
    /// <returns>The number after lastNo in month, or sequence 1 when lastNo is from another month.</returns>
    public static string Next(string prefix, DateOnly month, string? lastNo, int digits)
    {
        var head = prefix + month.ToString("yyyyMM", CultureInfo.InvariantCulture);
        long sequence = 1;
        if (lastNo is not null && lastNo.Length == head.Length + digits && lastNo.StartsWith(head, StringComparison.Ordinal)
            && long.TryParse(lastNo.AsSpan(head.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var last))
        {
            sequence = last + 1;
        }
        var text = sequence.ToString(CultureInfo.InvariantCulture);
        if (text.Length > digits)
        {
            throw new InvalidOperationException($"Document number sequence for {head} is exhausted.");
        }
        return head + text.PadLeft(digits, '0');
    }
}
