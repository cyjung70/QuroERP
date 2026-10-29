using QuroErp.Contracts.Bc.Codes;

namespace QuroErp.Web.Components.Pages.Bc.Models;

/// <summary>A grid row on the code management screen (BC-UC-008). The Minor code is entered only on a new row (SD-BR-912).</summary>
public sealed class CodeMinorRow
{
    public long Id { get; set; }
    public string? MinorCode { get; set; }

    /// <summary>Name by language code, with a key for every language in use. A blank name means no translation in that language.</summary>
    public Dictionary<string, string?> Names { get; set; } = [];

    public decimal? SortOrder { get; set; }
    public bool UseYn { get; set; } = true;
    public string? Attr1 { get; set; }
    public string? Attr2 { get; set; }

    /// <summary>The row's updatedAt when it was read; null for a new row.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>A new row in use, with an empty name in every language in use.</summary>
    public static CodeMinorRow New(IEnumerable<string> languages) => new() { Names = languages.ToDictionary(l => l, _ => (string?)null) };

    /// <summary>
    /// A loaded row with the names of the languages in use only. Names in other languages are not sent back, so a save keeps them
    /// (BC-BR-405).
    /// </summary>
    public static CodeMinorRow From(CodeMinorDto dto, IEnumerable<string> languages) =>
        new()
        {
            Id = dto.Id,
            MinorCode = dto.MinorCode,
            Names = languages.ToDictionary(l => l, l => dto.Names.GetValueOrDefault(l)),
            SortOrder = dto.SortOrder,
            UseYn = dto.UseYn,
            Attr1 = dto.Attr1,
            Attr2 = dto.Attr2,
            UpdatedAt = dto.UpdatedAt,
        };

    /// <summary>Grid changes of one Major as one save request. Loaded rows always have updatedAt; the API normalizes and checks every value.</summary>
    public static SaveCodeMinorsRequest ToRequest(
        string majorCode, IReadOnlyList<CodeMinorRow> added, IReadOnlyList<CodeMinorRow> updated, IReadOnlyList<CodeMinorRow> deleted) =>
        new(
            majorCode,
            [.. added.Select(r => new CodeMinorInsert(r.MinorCode, r.Names, WholeSortOrder(r), r.UseYn, r.Attr1, r.Attr2))],
            [.. updated.Select(r => new CodeMinorUpdate(r.Id, r.MinorCode, r.Names, WholeSortOrder(r), r.UseYn, r.Attr1, r.Attr2, r.UpdatedAt!.Value))],
            [.. deleted.Select(r => new CodeMinorDelete(r.Id, r.UpdatedAt!.Value))]);

    // A fraction or a number below 1 goes as 0, which the API rejects as SD-E-913 with the row's code
    private static int? WholeSortOrder(CodeMinorRow row) => row.SortOrder switch
    {
        null => null,
        var value when value == decimal.Truncate(value.Value) && value is >= 1 and <= int.MaxValue => (int)value.Value,
        _ => 0,
    };
}
