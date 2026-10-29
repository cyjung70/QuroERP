using QuroErp.Contracts.Bc.Codes;

namespace QuroErp.Web.Components.Pages.Bc.Models;

/// <summary>A grid row on the Major code screen (BC-UC-023). The Major code is entered only on a new row (SD-BR-902).</summary>
public sealed class CodeMajorRow
{
    public long Id { get; set; }
    public string? MajorCode { get; set; }

    /// <summary>Name by language code, with a key for every language in use. A blank name removes that language's translation.</summary>
    public Dictionary<string, string?> Names { get; set; } = [];

    public bool UseYn { get; set; } = true;

    /// <summary>Minor codes of the Major, in use or not; 0 on a new row. A Major with any cannot be deleted (SD-BR-903).</summary>
    public int MinorCount { get; set; }

    /// <summary>The row's updatedAt when it was read; null for a new row.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>A new row in use, with an empty name in every language in use.</summary>
    public static CodeMajorRow New(IEnumerable<string> languages) => new() { Names = languages.ToDictionary(l => l, _ => (string?)null) };

    /// <summary>
    /// A loaded row with the names of the languages in use only. Names in other languages are not sent back, so a save keeps them
    /// (BC-BR-405).
    /// </summary>
    public static CodeMajorRow From(CodeMajorDto dto, IEnumerable<string> languages) =>
        new()
        {
            Id = dto.Id,
            MajorCode = dto.MajorCode,
            Names = languages.ToDictionary(l => l, l => dto.Names.GetValueOrDefault(l)),
            UseYn = dto.UseYn,
            MinorCount = dto.MinorCount,
            UpdatedAt = dto.UpdatedAt,
        };

    /// <summary>Grid changes as one save request. Loaded rows always have updatedAt; the API normalizes and checks every value.</summary>
    public static SaveCodeMajorsRequest ToRequest(IReadOnlyList<CodeMajorRow> added, IReadOnlyList<CodeMajorRow> updated, IReadOnlyList<CodeMajorRow> deleted) =>
        new(
            [.. added.Select(r => new CodeMajorInsert(r.MajorCode, r.Names, r.UseYn))],
            [.. updated.Select(r => new CodeMajorUpdate(r.Id, r.MajorCode, r.Names, r.UseYn, r.UpdatedAt!.Value))],
            [.. deleted.Select(r => new CodeMajorDelete(r.Id, r.UpdatedAt!.Value))]);
}
