namespace QuroErp.Contracts.Bc.Codes;

/// <summary>Major code management API routes (BC-UC-023).</summary>
public static class CodeMajorRoutes
{
    /// <summary>GET ?majorCode=&amp;lang= (Majors whose code starts with majorCode, in code order), PUT the grid changes in one transaction (SD-BR-905).</summary>
    public const string Base = "api/sys/code-majors";
}

/// <summary>A Major code (sys_code_major) with its names (sys_code_major_translation) and the number of its Minor codes.</summary>
/// <param name="Name">Name in the requested language, falling back to the company default language, then en (BC-BR-402).</param>
/// <param name="Names">Name by language code, for every language that has one. en is always there (SD-BR-901).</param>
/// <param name="MinorCount">Minor codes of the Major, in use or not. A Major with any cannot be deleted (SD-BR-903).</param>
public sealed record CodeMajorDto(
    long Id, string MajorCode, string? Name, IReadOnlyDictionary<string, string> Names, bool UseYn, int MinorCount, DateTimeOffset UpdatedAt);

/// <summary>Grid changes, saved together or not at all (SD-BR-905).</summary>
public sealed record SaveCodeMajorsRequest(
    IReadOnlyList<CodeMajorInsert>? Inserted, IReadOnlyList<CodeMajorUpdate>? Updated, IReadOnlyList<CodeMajorDelete>? Deleted);

/// <summary>A new Major code. Names need en; a blank name leaves that language without a translation.</summary>
/// <param name="UseYn">Null means in use.</param>
public sealed record CodeMajorInsert(string? MajorCode, IReadOnlyDictionary<string, string?>? Names, bool? UseYn);

/// <summary>
/// A changed Major code. Names replace all of the code's names. The Major code cannot change (SD-BR-902): send the stored code or none.
/// updatedAt is the row's value when it was read (409 when stale).
/// </summary>
public sealed record CodeMajorUpdate(long Id, string? MajorCode, IReadOnlyDictionary<string, string?>? Names, bool? UseYn, DateTimeOffset UpdatedAt);

public sealed record CodeMajorDelete(long Id, DateTimeOffset UpdatedAt);
