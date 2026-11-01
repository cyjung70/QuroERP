using System.Text.Json.Serialization;
using QuroErp.Contracts.Common;

namespace QuroErp.Contracts.Bc.BusinessPlaces;

/// <summary>Business place routes (bc_business_place, BC-BR-319). There is no POST or DELETE in the MVP (405).</summary>
public static class BusinessPlaceRoutes
{
    public const string Base = "api/bc/business-places";

    public const string ById = Base + "/{id:long}";

    public static string For(long id) => $"{Base}/{id}";
}

/// <summary>A business place: the tax invoice supplier of its documents (BC-BR-320).</summary>
public sealed record BusinessPlaceDto(
    long Id, string BusinessPlaceCode, string BusinessPlaceName, bool IsDefault, string? BusinessRegNo, string? SubBusinessNo,
    string? CeoName, string? BusinessType, string? BusinessItem, string? Addr1, string? Addr2, string? City, string? State,
    string? ZipCode, string? TelNo, bool UseYn, DateTimeOffset CreatedAt, string CreatedBy, DateTimeOffset UpdatedAt, string UpdatedBy)
    : AuditInfo(CreatedAt, CreatedBy, UpdatedAt, UpdatedBy);

/// <summary>PUT body. The code and the default flag cannot be changed, so unknown properties are rejected with 400.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateBusinessPlaceRequest(
    string? BusinessPlaceName, string? BusinessRegNo, string? SubBusinessNo, string? CeoName, string? BusinessType, string? BusinessItem,
    string? Addr1, string? Addr2, string? City, string? State, string? ZipCode, string? TelNo, bool UseYn, DateTimeOffset UpdatedAt);
