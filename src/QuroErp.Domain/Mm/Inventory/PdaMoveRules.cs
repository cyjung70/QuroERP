using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Labels;

namespace QuroErp.Domain.Mm.Inventory;

/// <summary>Scans of the PDA move (MM-UC-039, MM-BR-922, MM-BR-923): where the stock comes from and where it goes.</summary>
public static class PdaMoveRules
{
    /// <summary>The sending step: a Location label, or a Pallet label to move the Pallet whole.</summary>
    public const string From = "FROM";

    /// <summary>The receiving step: a Location label.</summary>
    public const string To = "TO";

    /// <summary>A step other than FROM or TO. Args: field name, value.</summary>
    public const string InvalidStepCode = "Common-E-002";

    /// <summary>The label kinds a step takes (MM-BR-922): FROM takes LC and PL, TO takes LC.</summary>
    /// <exception cref="DomainException">Common-E-002 with "step" and the value for another step.</exception>
    public static IReadOnlyList<string> KindsOf(string? step) => step?.Trim().ToUpperInvariant() switch
    {
        From => [LabelCode.Location, LabelCode.Pallet],
        To => [LabelCode.Location],
        _ => throw new DomainException(InvalidStepCode, "step", step ?? ""),
    };

    /// <summary>
    /// MM-BR-923: a scanned place can be used when it exists and is in use: the Location and its warehouse in use, and a Pallet
    /// ACTIVE (a closed Pallet holds nothing to move, MM-BR-912).
    /// </summary>
    /// <exception cref="DomainException">MM-E-923 with the scanned value.</exception>
    public static void EnsurePlace(bool found, bool warehouseInUse, bool locationInUse, string? palletStatus, string scanned)
    {
        if (!found || !warehouseInUse || !locationInUse || (palletStatus is not null && palletStatus != PalletRules.Active))
        {
            throw new DomainException(LabelCode.UnknownCode, scanned);
        }
    }
}
