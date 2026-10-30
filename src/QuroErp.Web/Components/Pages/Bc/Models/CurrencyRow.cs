using QuroErp.Contracts.Bc.Localization;
using QuroErp.Contracts.Common;
using QuroErp.Web.Components.Screens;

namespace QuroErp.Web.Components.Pages.Bc.Models;

/// <summary>A grid row on the currency screen (BC-UC-026). The currency code is entered only on a new row.</summary>
public sealed class CurrencyRow
{
    public long Id { get; set; }
    public string? CurrencyCode { get; set; }

    /// <summary>Name by language code, with a key for every language in use. A blank name means no translation in that language.</summary>
    public Dictionary<string, string?> Names { get; set; } = [];

    /// <summary>Amount decimals; blank on a new row means the ISO 4217 value (BC-BR-411).</summary>
    public decimal? Decimals { get; set; }

    public string? CountryCode { get; set; }
    public bool UseYn { get; set; } = true;

    /// <summary>The row's updatedAt when it was read; null for a new row.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>A new row in use, with an empty name in every language in use.</summary>
    public static CurrencyRow New(IEnumerable<string> languages) => new() { Names = languages.ToDictionary(l => l, _ => (string?)null) };

    /// <summary>
    /// A loaded row with the names of the languages in use only. Names in other languages are not sent back, so a save keeps them
    /// (BC-BR-405).
    /// </summary>
    public static CurrencyRow From(CurrencyMasterDto dto, IEnumerable<string> languages) =>
        new()
        {
            Id = dto.Id,
            CurrencyCode = dto.CurrencyCode,
            Names = languages.ToDictionary(l => l, l => dto.Names.GetValueOrDefault(l)),
            Decimals = dto.Decimals,
            CountryCode = dto.CountryCode,
            UseYn = dto.UseYn,
            UpdatedAt = dto.UpdatedAt,
        };

    /// <summary>Grid changes as one batch request. Loaded rows always have updatedAt; the API normalizes and checks every value.</summary>
    public static BatchRequest<CurrencySaveRow, CurrencyKey> ToRequest(BatchChanges<CurrencyRow> changes) =>
        new(
            [.. changes.Added.Select(r => new CurrencySaveRow(null, r.CurrencyCode, WholeDecimals(r), r.CountryCode, r.UseYn, r.Names, null))],
            [.. changes.Updated.Select(r => new CurrencySaveRow(r.Id, r.CurrencyCode, WholeDecimals(r), r.CountryCode, r.UseYn, r.Names, r.UpdatedAt))],
            [.. changes.Deleted.Select(r => new CurrencyKey(r.Id, r.UpdatedAt!.Value))]);

    // A fraction or a value out of range goes as -1, which the API rejects as BC-E-411 with the row's currency
    private static int? WholeDecimals(CurrencyRow row) => row.Decimals switch
    {
        null => null,
        var value when value == decimal.Truncate(value.Value) && value is >= 0 and <= 4 => (int)value.Value,
        _ => -1,
    };
}
