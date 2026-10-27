using System.Data;
using System.Globalization;
using QuroErp.Domain.Common;

namespace QuroErp.Application.Common.DocumentNumbers;

/// <summary>
/// Issues document numbers prefix + yyyyMM + sequence in the caller's transaction (layers.md "트랜잭션").
/// The month is the document date's month. Each month has its own sys_document_number row
/// (trans_type = prefix + yyyyMM), so back-dated documents continue that month's sequence.
/// </summary>
public sealed class DocumentNumberService(IDocumentNumberRepository numbers)
{
    /// <summary>Issues one number per document date, in the order given, locking each month's counter once.</summary>
    public async Task<IReadOnlyList<string>> NextAsync(
        string prefix, int digits, IReadOnlyList<DateOnly> documentDates, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var issued = new string[documentDates.Count];
        // Months in a fixed order so two documents cannot deadlock on the counters
        foreach (var month in documentDates.Select(d => new DateOnly(d.Year, d.Month, 1)).Distinct().Order())
        {
            var transType = prefix + month.ToString("yyyyMM", CultureInfo.InvariantCulture);
            var last = await numbers.LockLastAsync(transType, user, transaction, cancellationToken);
            for (var i = 0; i < documentDates.Count; i++)
            {
                if (documentDates[i].Year == month.Year && documentDates[i].Month == month.Month)
                {
                    last = DocumentNumber.Next(prefix, month, last, digits);
                    issued[i] = last;
                }
            }
            await numbers.SaveLastAsync(transType, last!, user, transaction, cancellationToken);
        }
        return issued;
    }
}
