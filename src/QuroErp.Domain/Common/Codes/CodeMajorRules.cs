using System.Text.RegularExpressions;

namespace QuroErp.Domain.Common.Codes;

/// <summary>Major code fields after normalizing. Names are by language code; every name has text.</summary>
public sealed record CodeMajorFields(IReadOnlyDictionary<string, string> Names, bool UseYn);

/// <summary>Major code rules (SD-BR-901~904). Every error names the Major code of the failing row.</summary>
public static partial class CodeMajorRules
{
    public const string InvalidCodeCode = "SD-E-901";
    public const string CodeChangedCode = "SD-E-902";
    public const string HasMinorsCode = "SD-E-903";
    public const string ProtectedCode = "SD-E-904";

    public const int MaxCodeLength = 30;

    /// <summary>SD-BR-904: the Majors that code refers to as constants (Domain CodeMajors). They cannot be deleted or set not in use.</summary>
    public static readonly IReadOnlySet<string> ProtectedMajors = new HashSet<string>(StringComparer.Ordinal)
    {
        CodeMajors.Grade, CodeMajors.ItemClass, CodeMajors.ItemType, CodeMajors.MovementType, CodeMajors.Plating, CodeMajors.Unit,
        CodeMajors.ValidFlag,
    };

    /// <summary>SD-BR-901: required; upper case letters, digits and _; at most 30 characters.</summary>
    public static string NormalizeCode(string? majorCode)
    {
        var code = majorCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code) || code.Length > MaxCodeLength || !CodePattern().IsMatch(code))
        {
            throw new DomainException(InvalidCodeCode, majorCode ?? "");
        }
        return code;
    }

    /// <summary>
    /// SD-BR-901 for one row. Blank names are dropped (no translation in that language), but the en name is required.
    /// A missing in-use flag means in use.
    /// </summary>
    public static CodeMajorFields Normalize(string majorCode, IReadOnlyDictionary<string, string?>? names, bool? useYn) =>
        new(CodeNames.Normalize(names, InvalidCodeCode, majorCode), useYn ?? true);

    /// <summary>SD-BR-902: a stored Major code never changes. A blank code in a change keeps the stored one.</summary>
    public static void EnsureSameCode(string storedCode, string? requestedCode)
    {
        if (!string.IsNullOrWhiteSpace(requestedCode) && !string.Equals(requestedCode.Trim(), storedCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException(CodeChangedCode, storedCode);
        }
    }

    /// <summary>SD-BR-901: codes are unique among the Majors the save leaves.</summary>
    public static void EnsureUnique(IReadOnlyCollection<string> majorCodes)
    {
        if (majorCodes.GroupBy(c => c, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } code)
        {
            throw new DomainException(InvalidCodeCode, code.Key);
        }
    }

    /// <summary>SD-BR-904: a constant Major cannot be deleted or set not in use.</summary>
    public static void EnsureNotProtected(string majorCode)
    {
        if (ProtectedMajors.Contains(majorCode))
        {
            throw new DomainException(ProtectedCode, majorCode);
        }
    }

    /// <summary>SD-BR-903, 904: a constant Major or one that still has Minor codes cannot be deleted.</summary>
    public static void EnsureDeletable(string majorCode, int minorCount)
    {
        EnsureNotProtected(majorCode);
        if (minorCount > 0)
        {
            throw new DomainException(HasMinorsCode, majorCode);
        }
    }

    [GeneratedRegex("^[A-Z0-9_]+$")]
    private static partial Regex CodePattern();
}
