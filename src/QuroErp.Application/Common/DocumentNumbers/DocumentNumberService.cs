using System.Data;
using QuroErp.Domain.Common;

namespace QuroErp.Application.Common.DocumentNumbers;

/// <summary>
/// Issues document numbers in the caller's transaction (SD-BR-006, layers.md "트랜잭션", AS IS get_auto_num).
/// The counter row stays locked until the transaction ends, so concurrent saves never get the same number and a
/// rolled-back save gives its number back. Monthly formats use the document date's month, so back-dated
/// documents continue that month's sequence.
/// </summary>
public sealed class DocumentNumberService(IDocumentNumberRepository numbers)
{
    /// <summary>Issues the number of one document.</summary>
    /// <exception cref="DomainException">SD-E-006 when the sequence has no more digits.</exception>
    public async Task<string> NextAsync(
        DocumentNumberFormat format, DateOnly documentDt, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await NextAsync(format, [documentDt], user, transaction, cancellationToken))[0];

    /// <summary>Issues one number per document date, in the order given, locking each counter once.</summary>
    /// <exception cref="DomainException">SD-E-006 when the sequence has no more digits.</exception>
    public async Task<IReadOnlyList<string>> NextAsync(
        DocumentNumberFormat format, IReadOnlyList<DateOnly> documentDates, string user, IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        var issued = new string[documentDates.Count];
        // Counters in a fixed order so two documents cannot deadlock on them
        foreach (var period in documentDates.Select(format.Period).Distinct().Order(StringComparer.Ordinal))
        {
            var indexes = Enumerable.Range(0, documentDates.Count).Where(i => format.Period(documentDates[i]) == period).ToList();
            var last = await numbers.LockLastAsync(format.Prefix, period, transaction, cancellationToken);
            var start = 0;
            if (last is null)
            {
                var first = format.Next(documentDates[indexes[0]], null);
                if (await numbers.TryCreateAsync(format.Prefix, period, first, user, transaction, cancellationToken))
                {
                    issued[indexes[0]] = last = first;
                    start = 1;
                }
                else
                {
                    // Another transaction created the counter and has ended, so the row exists now
                    last = await numbers.LockLastAsync(format.Prefix, period, transaction, cancellationToken);
                }
            }
            foreach (var i in indexes.Skip(start))
            {
                issued[i] = last = format.Next(documentDates[i], last);
            }
            if (start < indexes.Count)
            {
                await numbers.SaveLastAsync(format.Prefix, period, last!, user, transaction, cancellationToken);
            }
        }
        return issued;
    }
}
