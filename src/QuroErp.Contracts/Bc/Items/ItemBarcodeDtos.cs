namespace QuroErp.Contracts.Bc.Items;

/// <summary>Item barcode API routes (BC-UC-011).</summary>
public static class ItemBarcodeRoutes
{
    public const string Base = "api/bc/item-barcodes";
    public const string Preview = "preview";
    public const string Generate = "generate";

    /// <summary>validFlag value for items in use or not (code DISPLAY_OPTIONS).</summary>
    public const string AllValidFlags = "ALL";
}

/// <summary>Items of one item class; validFlag Y, N or ALL (default Y).</summary>
public sealed record ItemBarcodeQuery
{
    public string? ItemClass { get; init; }

    public string? ValidFlag { get; init; }
}

/// <summary>A barcode generation run; StartNo defaults to 1000000000.</summary>
public sealed record ItemBarcodePreviewQuery
{
    public string? ItemClass { get; init; }

    public string? ValidFlag { get; init; }

    public string? StartNo { get; init; }
}

public sealed record GenerateItemBarcodesRequest(string? ItemClass, string? ValidFlag, string? StartNo);

public sealed record ItemBarcodeDto(long Id, string ItemCd, string? ItemName, decimal SoUsdPrc, string? UpcCode);

/// <param name="OtherItemCd">The item that already has <paramref name="UpcCode"/>.</param>
public sealed record ItemBarcodeConflictDto(string ItemCd, string UpcCode, string OtherItemCd);

/// <summary>What a generation run would do (MM-BR-815~817); nothing is saved.</summary>
/// <param name="OverwriteCount">Selected items that already have a barcode.</param>
/// <param name="Items">The selected items with the barcode each would get, in item code order.</param>
public sealed record ItemBarcodePreviewDto(
    int TargetCount, int OverwriteCount, IReadOnlyList<ItemBarcodeDto> Items, IReadOnlyList<ItemBarcodeConflictDto> Conflicts);

public sealed record GenerateItemBarcodesResultDto(int UpdatedCount);
