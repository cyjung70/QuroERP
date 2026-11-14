using System.Text.RegularExpressions;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Mm.Locations;

/// <summary>A Location (shelf) as it will be stored (bc_location, MM-BR-712). Id is 0 for a new one.</summary>
public sealed record LocationState(
    long Id, long WarehouseId, string LocationCode, string? LocationName, bool IsSystem, bool UseYn, string? Remarks);

/// <summary>Why a Location cannot be set not in use or deleted: the second arg of MM-E-717 (MM-BR-717).</summary>
public static class LocationBlockReasons
{
    /// <summary>An item has a balance other than 0 on the Location.</summary>
    public const string OnHand = "ONHAND";

    /// <summary>The Location has stock movements, so it cannot be deleted.</summary>
    public const string HasMovement = "HAS_MOVEMENT";
}

/// <summary>Location master rules (MM-UC-034, MM-BR-712, MM-BR-717).</summary>
public static partial class LocationRules
{
    /// <summary>
    /// The code is not valid, is used twice in one warehouse or changed, or the system Location '*' was changed, deleted or set not
    /// in use. Arg: the code.
    /// </summary>
    public const string InvalidCode = "MM-E-712";

    /// <summary>A Location with stock or movements cannot be set not in use or deleted. Args: the code, a <see cref="LocationBlockReasons"/> value.</summary>
    public const string InUseCode = "MM-E-717";

    /// <summary>The system Location of every warehouse; items without Location management keep their stock here (MM-BR-713).</summary>
    public const string SystemCode = "*";

    public const int CodeMaxLength = 20;

    /// <summary>MM-BR-712: trimmed and in upper case; 1 to 20 characters of A-Z, 0-9, '-' and '_'. Users never add '*'.</summary>
    /// <exception cref="DomainException">MM-E-712 with the code as given.</exception>
    public static string NormalizeCode(string? code)
    {
        var normalized = code?.Trim().ToUpperInvariant() ?? "";
        return CodePattern().IsMatch(normalized) ? normalized : throw new DomainException(InvalidCode, code?.Trim() ?? "");
    }

    /// <summary>Empty name and remarks are stored as null.</summary>
    public static LocationState Normalize(LocationState location) => location with
    {
        LocationName = string.IsNullOrWhiteSpace(location.LocationName) ? null : location.LocationName.Trim(),
        Remarks = string.IsNullOrWhiteSpace(location.Remarks) ? null : location.Remarks.Trim(),
    };

    /// <summary>
    /// MM-BR-712 for a changed row: the system Location '*' never changes, and the code stays as stored. An empty code in the
    /// request means unchanged.
    /// </summary>
    /// <exception cref="DomainException">MM-E-712 with the code.</exception>
    public static void EnsureChangeable(LocationState stored, string? requestedCode)
    {
        if (stored.IsSystem)
        {
            throw new DomainException(InvalidCode, stored.LocationCode);
        }
        if (!string.IsNullOrWhiteSpace(requestedCode) && !string.Equals(requestedCode.Trim(), stored.LocationCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException(InvalidCode, requestedCode.Trim());
        }
    }

    /// <summary>MM-BR-712: the system Location '*' is never deleted.</summary>
    /// <exception cref="DomainException">MM-E-712 with the code.</exception>
    public static void EnsureDeletable(LocationState stored)
    {
        if (stored.IsSystem)
        {
            throw new DomainException(InvalidCode, stored.LocationCode);
        }
    }

    /// <summary>MM-BR-712: (warehouse, code) is unique among the Locations as the batch save leaves them.</summary>
    /// <exception cref="DomainException">MM-E-712 with the first repeated code.</exception>
    public static void EnsureUnique(IEnumerable<LocationState> locations)
    {
        if (locations.GroupBy(l => (l.WarehouseId, l.LocationCode)).FirstOrDefault(g => g.Count() > 1) is { } duplicate)
        {
            throw new DomainException(InvalidCode, duplicate.Key.LocationCode);
        }
    }

    [GeneratedRegex("^[A-Z0-9_-]{1,20}$")]
    private static partial Regex CodePattern();
}
