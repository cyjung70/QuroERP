using System.Data;

namespace QuroErp.Application.Bc.Codes;

/// <summary>A stored Major code without its names.</summary>
/// <param name="MinorCount">Minor codes of the Major, in use or not.</param>
public sealed record CodeMajorRecord(long Id, string MajorCode, bool UseYn, int MinorCount, DateTimeOffset UpdatedAt);

public interface ICodeMajorRepository
{
    /// <summary>
    /// Majors whose code starts with codePrefix (every Major when null), in code order. forUpdate locks the rows, so saves run one at a time
    /// and no Minor code is added to a Major while a save checks it (Minor saves lock their Major row too).
    /// </summary>
    Task<IReadOnlyList<CodeMajorRecord>> FindAsync(string? codePrefix, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken);

    Task<long> InsertAsync(string majorCode, bool useYn, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <returns>False when the row changed after expectedUpdatedAt.</returns>
    Task<bool> UpdateAsync(
        long id, bool useYn, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Makes the code's translations exactly the given names.</summary>
    Task ReplaceNamesAsync(
        long majorId, IReadOnlyDictionary<string, string> names, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Deletes the rows; their translations go with them.</summary>
    Task DeleteAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken);
}
