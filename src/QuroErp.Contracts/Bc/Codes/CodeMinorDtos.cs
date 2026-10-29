namespace QuroErp.Contracts.Bc.Codes;

/// <summary>Minor code management API routes (BC-UC-008).</summary>
public static class CodeMinorRoutes
{
    /// <summary>GET ?majorCode=&amp;lang= (every Minor code of the Major in sort order), PUT the grid changes in one transaction (SD-BR-905).</summary>
    public const string Base = "api/bc/codes";
}

/// <summary>A Minor code (sys_code_minor) with its names (sys_code_minor_translation).</summary>
/// <param name="Name">Name in the requested language, falling back to the company default language, then en (BC-BR-402).</param>
/// <param name="Names">Name by language code, for every language that has one. en is always there (SD-BR-911).</param>
/// <param name="UseYn">False: left out of code lists and rejected by new saves (SD-BR-915).</param>
public sealed record CodeMinorDto(
    long Id, string MinorCode, string? Name, IReadOnlyDictionary<string, string> Names, int SortOrder, bool UseYn, string? Attr1, string? Attr2,
    DateTimeOffset UpdatedAt);

/// <summary>Grid changes for one Major, saved together or not at all (SD-BR-905).</summary>
public sealed record SaveCodeMinorsRequest(
    string? MajorCode, IReadOnlyList<CodeMinorInsert>? Inserted, IReadOnlyList<CodeMinorUpdate>? Updated, IReadOnlyList<CodeMinorDelete>? Deleted);

/// <summary>A new Minor code. Names need en; a blank name leaves that language without a translation.</summary>
/// <param name="UseYn">Null means in use.</param>
public sealed record CodeMinorInsert(
    string? MinorCode, IReadOnlyDictionary<string, string?>? Names, int? SortOrder, bool? UseYn, string? Attr1, string? Attr2);

/// <summary>
/// A changed Minor code. Names replace all of the code's names. The Minor code cannot change (SD-BR-912): send the stored code or none.
/// updatedAt is the row's value when it was read (409 when stale).
/// </summary>
public sealed record CodeMinorUpdate(
    long Id, string? MinorCode, IReadOnlyDictionary<string, string?>? Names, int? SortOrder, bool? UseYn, string? Attr1, string? Attr2,
    DateTimeOffset UpdatedAt);

public sealed record CodeMinorDelete(long Id, DateTimeOffset UpdatedAt);
