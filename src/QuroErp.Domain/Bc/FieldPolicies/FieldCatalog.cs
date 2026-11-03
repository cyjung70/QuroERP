using QuroErp.Domain.Bc.Localization;

namespace QuroErp.Domain.Bc.FieldPolicies;

/// <summary>What a field's default value must be (BC-BR-437 "기본값이 코드 목록·형식에 맞다").</summary>
public enum FieldValueKind
{
    /// <summary>Any text.</summary>
    Text,

    /// <summary>A supported country code (<see cref="CountryProfiles.Supported"/>).</summary>
    Country,
}

/// <param name="Key">The field key: the label resource key, {Module}.{Screen}.{Property}.</param>
/// <param name="IsRequiredBusinessField">The specification makes it required and logic reads it: it can never be HIDDEN (BC-BR-433).</param>
public sealed record CatalogField(string Key, bool IsRequiredBusinessField, FieldValueKind Kind = FieldValueKind.Text);

/// <summary>
/// The fields that take a field policy, by screen (field-policy.md "필드 키"). Screens add their fields here when they adopt the
/// policy. Required business fields are the ones the screen specification marks 필수.
/// </summary>
public static class FieldCatalog
{
    /// <summary>Country-only input fields: {screen}.ext.{name}, declared by the country pack (BC-BR-436).</summary>
    public const string ExtSegment = "ext";

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<CatalogField>> Screens =
        new Dictionary<string, IReadOnlyList<CatalogField>>(StringComparer.Ordinal)
        {
            // SD customer.md "화면 필드"
            ["Sd.Customer"] = Screen("Sd.Customer",
                required: ["CName", "ShipName", "Phone1", "Rep", "Terms", "ZipCode"],
                optional: ["Contact", "Contact2", "Phone2", "Phone3", "FaxNo", "Email", "PriceLevel", "FreightCharge", "BalAmt", "Remarks",
                    "Memo2", "Memo3", "ValidFlag", "Addr1", "Addr2", "City", "State", "Country", "ShipAddr1", "ShipAddr2", "ShipCity",
                    "ShipState", "ShipCountry", "ShipZipCode", "ShipPhone", "ShipFax", "CreditLimit", "DcStatus", "StartDt",
                    "BusinessRegNo", "CeoName", "BusinessType", "BusinessItem", "TaxExemptions"],
                countries: ["Country", "ShipCountry"]),
            // MM vendor.md "화면 필드"
            ["Mm.Vendor"] = Screen("Mm.Vendor",
                required: ["VName", "Phone1", "ZipCode"],
                optional: ["Contact", "Contact2", "Phone2", "Phone3", "FaxNo", "Email", "Terms", "ValidFlag", "BalAmt", "Remarks", "Addr1",
                    "Addr2", "City", "State", "Country", "ShipAddr1", "ShipAddr2", "ShipCity", "ShipState", "ShipCountry", "ShipZipCode",
                    "VendorPrice", "BusinessRegNo", "CeoName", "BusinessType", "BusinessItem"],
                countries: ["Country", "ShipCountry"]),
        };

    private static readonly IReadOnlyDictionary<string, CatalogField> ByKey =
        Screens.Values.SelectMany(f => f).ToDictionary(f => f.Key, StringComparer.Ordinal);

    public static IReadOnlyCollection<string> ScreenCodes => (IReadOnlyCollection<string>)Screens.Keys;

    /// <summary>The screen's fields, or null when the screen does not take field policies.</summary>
    public static IReadOnlyList<CatalogField>? FieldsOf(string screenCode) => Screens.GetValueOrDefault(screenCode);

    public static CatalogField? Find(string fieldKey) => ByKey.GetValueOrDefault(fieldKey);

    /// <summary>The screen code of a catalog field or a declared ext. key, or null.</summary>
    public static string? ScreenOf(string fieldKey) =>
        Screens.Keys.FirstOrDefault(screen => fieldKey.StartsWith(screen + ".", StringComparison.Ordinal));

    /// <summary>{screen}.ext.{name} keys.</summary>
    public static bool IsExtKey(string fieldKey) =>
        ScreenOf(fieldKey) is { } screen && fieldKey.Length > screen.Length + ExtSegment.Length + 2
        && fieldKey.AsSpan(screen.Length + 1).StartsWith(ExtSegment + ".", StringComparison.Ordinal);

    public static string ExtKey(string screenCode, string name) => $"{screenCode}.{ExtSegment}.{name}";

    public static string Key(string screenCode, string property) => $"{screenCode}.{property}";

    private static IReadOnlyList<CatalogField> Screen(string screenCode, string[] required, string[] optional, string[] countries) =>
    [
        .. required.Select(p => new CatalogField(Key(screenCode, p), true, Kind(p, countries))),
        .. optional.Select(p => new CatalogField(Key(screenCode, p), false, Kind(p, countries))),
    ];

    private static FieldValueKind Kind(string property, string[] countries) =>
        countries.Contains(property, StringComparer.Ordinal) ? FieldValueKind.Country : FieldValueKind.Text;
}
