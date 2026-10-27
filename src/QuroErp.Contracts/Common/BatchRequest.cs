namespace QuroErp.Contracts.Common;

/// <summary>
/// Batch save of a Multi (M) screen: <c>POST /api/{module}/{resource}/batch</c> (screens.md "유형별 저장 API 형태").
/// Only changed rows are sent.
/// </summary>
/// <param name="Added">New rows (row state I).</param>
/// <param name="Updated">Changed rows (row state U).</param>
/// <param name="Deleted">Keys of deleted rows (row state D).</param>
public sealed record BatchRequest<TRow, TKey>(IReadOnlyList<TRow> Added, IReadOnlyList<TRow> Updated, IReadOnlyList<TKey> Deleted);
