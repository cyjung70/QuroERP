using System.Text.Json.Serialization;
using QuroErp.Contracts.Common;

namespace QuroErp.Contracts.Bc.Companies;

/// <summary>
/// The company (BC-UC-007, BC-BR-301). HasLogo says whether GET logo returns an image. The registration number, business type and
/// item are the business place's (BC-BR-315).
/// </summary>
/// <param name="TimeZone">IANA name of the company time zone (BC-BR-318).</param>
public sealed record CompanyDto(
    string Name, string? Billto, string? Shipto, string? Contact, string? TelNo, string? FaxNo, string? Represent, string? Email,
    string CountryCode, string BaseCurrencyCode, string DefaultLangCode,
    string TimeZone, string BillingMode, bool HasLogo, DateTimeOffset CreatedAt, string CreatedBy, DateTimeOffset UpdatedAt, string UpdatedBy)
    : AuditInfo(CreatedAt, CreatedBy, UpdatedAt, UpdatedBy);

/// <summary>
/// PUT body. Unknown properties are rejected with 400, so the close month (BC-BR-307), logo and other settings
/// cannot be changed through it.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateCompanyRequest(
    string? Name, string? Billto, string? Shipto, string? Contact, string? TelNo, string? FaxNo, string? Represent, string? Email,
    string? CountryCode, string? BaseCurrencyCode, string? DefaultLangCode,
    string? TimeZone, string? BillingMode, DateTimeOffset UpdatedAt);
