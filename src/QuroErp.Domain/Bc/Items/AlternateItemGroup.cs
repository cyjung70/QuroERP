using QuroErp.Domain.Common;

namespace QuroErp.Domain.Bc.Items;

/// <summary>A clean group row: upper-case code and trimmed name (MM-BR-810).</summary>
public sealed record AlternateItemGroupFields(string AltItemGroupCd, string AltItemGroupName);

/// <summary>Alternate item groups (BC-UC-019, MM-BR-810, MM-BR-813, MM-BR-814).</summary>
public static class AlternateItemGroupRules
{
    /// <summary>MM-BR-810: the code is empty, longer than 18, or taken, or the name is empty. Arg: the code.</summary>
    public const string InvalidCode = "MM-E-810";

    /// <summary>MM-BR-814: a group that has items cannot be deleted (409). Arg: the code.</summary>
    public const string InUseCode = "MM-E-814";

    public const int CodeMaxLength = 18;

    /// <exception cref="DomainException">MM-E-810.</exception>
    public static AlternateItemGroupFields Normalize(string? code, string? name)
    {
        var clean = code?.Trim().ToUpperInvariant() ?? string.Empty;
        if (clean.Length is 0 or > CodeMaxLength || string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException(InvalidCode, clean);
        }
        return new AlternateItemGroupFields(clean, name.Trim());
    }

    /// <summary>
    /// The codes of the saved rows, in row order, must differ from each other and from the stored groups that stay (MM-BR-810). The
    /// first clash is reported (MM-BR-813).
    /// </summary>
    /// <param name="takenByOthers">Codes of stored groups that are neither changed nor deleted by this save.</param>
    /// <exception cref="DomainException">MM-E-810 with the clashing code.</exception>
    public static void EnsureUnique(IEnumerable<string> rowCodes, IReadOnlySet<string> takenByOthers)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var code in rowCodes)
        {
            if (takenByOthers.Contains(code) || !seen.Add(code))
            {
                throw new DomainException(InvalidCode, code);
            }
        }
    }
}
