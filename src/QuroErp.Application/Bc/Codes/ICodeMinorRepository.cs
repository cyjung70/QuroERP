using System.Data;
using QuroErp.Domain.Common.Codes;

namespace QuroErp.Application.Bc.Codes;

/// <summary>A stored Minor code without its names.</summary>
public sealed record CodeMinorRecord(
    long Id, string MinorCode, int SortOrder, bool UseYn, string? Attr1, string? Attr2, DateTimeOffset UpdatedAt);

public interface ICodeMinorRepository
{
    /// <summary>The Major's id, in use or not; null when it does not exist. forUpdate locks the Major row so saves of one Major run one at a time.</summary>
    Task<long?> FindMajorIdAsync(string majorCode, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Every Minor code of the Major, in sort order.</summary>
    Task<IReadOnlyList<CodeMinorRecord>> FindByMajorAsync(long majorId, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The given Minor codes that a business column of the Major stores (Domain CodeUsages, SD-BR-914).</summary>
    Task<IReadOnlySet<string>> FindUsedAsync(
        string majorCode, IReadOnlyCollection<string> minorCodes, IDbTransaction transaction, CancellationToken cancellationToken);

    Task<long> InsertAsync(
        long majorId, string minorCode, CodeMinorFields fields, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <returns>False when the row changed after expectedUpdatedAt.</returns>
    Task<bool> UpdateAsync(
        long id, CodeMinorFields fields, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Sets the given names and removes the null ones. Languages not given keep their names (BC-BR-405).</summary>
    Task SaveNamesAsync(
        long minorId, IReadOnlyDictionary<string, string?> names, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Deletes the rows; their translations go with them.</summary>
    Task DeleteAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken);
}
