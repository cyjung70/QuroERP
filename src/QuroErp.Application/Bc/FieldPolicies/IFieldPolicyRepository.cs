using System.Data;
using QuroErp.Contracts.Bc.FieldPolicies;
using QuroErp.Domain.Bc.FieldPolicies;

namespace QuroErp.Application.Bc.FieldPolicies;

/// <summary>Field policy company overrides (bc_field_policy). Callers pass their own transaction.</summary>
public interface IFieldPolicyRepository
{
    /// <summary>Every override in field_key order.</summary>
    Task<IReadOnlyList<FieldPolicyOverrideDto>> FindAllAsync(IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Inserts or replaces the override of fieldKey.</summary>
    Task UpsertAsync(string fieldKey, FieldPolicyEntry entry, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken);

    Task DeleteAsync(IReadOnlyCollection<string> fieldKeys, IDbTransaction transaction, CancellationToken cancellationToken);
}

/// <summary>The country packs built into the application (countrypacks/{country}/).</summary>
public interface ICountryPackProvider
{
    IReadOnlyList<CountryPack> All { get; }

    /// <summary>The pack of the country, or null.</summary>
    CountryPack? Find(string? countryCode);
}
