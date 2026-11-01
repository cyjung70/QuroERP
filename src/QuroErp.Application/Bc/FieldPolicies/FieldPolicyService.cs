using System.Collections.Concurrent;
using System.ComponentModel;
using System.Data;
using System.Reflection;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Bc.FieldPolicies;
using QuroErp.Domain.Bc.FieldPolicies;
using QuroErp.Domain.Common;

namespace QuroErp.Application.Bc.FieldPolicies;

/// <summary>
/// Field policies (field-policy.md, BC-BR-430~436): the merged policy of a screen, the company overrides, and the save check that
/// uses the same policy as the screen. A DTO property P of screen S is the field S.P; country-only fields are the keys of an
/// <c>ExtAttrs</c> property of type <c>IReadOnlyDictionary&lt;string, string?&gt;</c>, the field S.ext.{key}.
/// </summary>
public sealed class FieldPolicyService(
    IFieldPolicyRepository overrides, ICountryPackProvider packs, ILocalizationRepository localization, IUnitOfWorkFactory unitOfWork)
{
    /// <summary>The DTO property that holds ext_attrs.</summary>
    public const string ExtAttrsProperty = "ExtAttrs";

    private const string RequiredCode = "Common-E-005";
    private const string NotFoundCode = "Common-E-006";

    private static readonly MethodInfo MemberwiseCloneMethod =
        typeof(object).GetMethod(nameof(MemberwiseClone), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly ConcurrentDictionary<Type, (PropertyInfo[] Fields, PropertyInfo? Ext)> Properties = new();

    /// <summary>The merged policies of a screen, for any signed-in user (BC-BR-431).</summary>
    /// <exception cref="BadRequestException">400 Common-E-005 when no screen is given.</exception>
    /// <exception cref="NotFoundException">404 Common-E-006 when the screen does not take field policies.</exception>
    public async Task<IReadOnlyList<FieldPolicyDto>> GetAsync(string? screenCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(screenCode))
        {
            throw new BadRequestException(RequiredCode, "screen");
        }
        if (FieldCatalog.FieldsOf(screenCode) is null)
        {
            throw new NotFoundException(NotFoundCode, screenCode);
        }
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var policy = await LoadAsync(screenCode, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return [.. policy.Fields.Select(f => new FieldPolicyDto(f.FieldKey, f.State, f.DefaultValue, f.LabelKey, f.Source.ToString()))];
    }

    /// <summary>The screen's policy with the company country's pack and the company overrides.</summary>
    public async Task<FieldPolicy> LoadAsync(string screenCode, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var company = await localization.FindCompanyAsync(transaction, cancellationToken);
        var stored = await overrides.FindAllAsync(transaction, cancellationToken);
        return FieldPolicy.Merge(
            screenCode,
            packs.Find(company?.CountryCode),
            stored.ToDictionary(o => o.FieldKey, o => new FieldPolicyEntry(o.State, o.DefaultValue, o.LabelKey), StringComparer.Ordinal));
    }

    /// <summary>Loads the screen's policy and applies <see cref="Validate{T}"/>.</summary>
    public async Task<T> ValidateAsync<T>(
        string screenCode, T input, T? stored, IDbTransaction transaction, CancellationToken cancellationToken) where T : class =>
        Validate(await LoadAsync(screenCode, transaction, cancellationToken), input, stored);

    /// <summary>
    /// The save check (BC-BR-432, BC-BR-434, BC-BR-436). Returns a copy of input where HIDDEN and READONLY fields have the policy
    /// default (new record, stored null) or the stored value (edit), and ext_attrs keeps the declared keys only. The input is not changed.
    /// </summary>
    /// <exception cref="DomainException">
    /// 422 BC-E-432 with the label key when a REQUIRED field is empty; 422 BC-E-436 with the field key for an undeclared ext_attrs key.
    /// </exception>
    public static T Validate<T>(FieldPolicy policy, T input, T? stored) where T : class
    {
        var (fields, ext) = PropertiesOf(typeof(T));
        var result = Clone(input);
        foreach (var property in fields)
        {
            var field = policy.Get(FieldCatalog.Key(policy.ScreenCode, property.Name));
            if (FieldState.IgnoresInput(field.State))
            {
                property.SetValue(result, stored is null ? DefaultOf(property.PropertyType, field.DefaultValue) : property.GetValue(stored));
            }
            else if (field.State == FieldState.Required && FieldPolicyRules.IsEmpty(property.GetValue(result)))
            {
                throw new DomainException(FieldPolicyRules.RequiredEmptyCode, field.LabelKey);
            }
        }
        if (ext is not null)
        {
            ext.SetValue(result, ValidateExt(policy, ExtOf(ext, input), stored is null ? null : ExtOf(ext, stored)));
        }
        return result;
    }

    /// <summary>A copy of a response DTO with the HIDDEN fields null and the HIDDEN ext_attrs keys removed (BC-BR-434).</summary>
    public static T Mask<T>(FieldPolicy policy, T dto) where T : class
    {
        var (fields, ext) = PropertiesOf(typeof(T));
        var result = Clone(dto);
        foreach (var property in fields.Where(p => !p.PropertyType.IsValueType || Nullable.GetUnderlyingType(p.PropertyType) is not null))
        {
            if (policy.Get(FieldCatalog.Key(policy.ScreenCode, property.Name)).State == FieldState.Hidden)
            {
                property.SetValue(result, null);
            }
        }
        if (ext is not null)
        {
            ext.SetValue(result, ExtOf(ext, dto)
                .Where(e => policy.Get(FieldCatalog.ExtKey(policy.ScreenCode, e.Key)).State != FieldState.Hidden)
                .ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal));
        }
        return result;
    }

    /// <summary>
    /// Saves company overrides, all or nothing (BC-BR-433); a row with every part blank removes the override. Full Access on the
    /// localization menu is checked by the API (BC-BR-435). Returns every override after the save.
    /// </summary>
    /// <exception cref="DomainException">422 BC-E-433 with the field key; nothing is saved.</exception>
    public async Task<IReadOnlyList<FieldPolicyOverrideDto>> SaveAsync(
        SaveFieldPoliciesRequest request, string updatedBy, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var company = await localization.FindCompanyAsync(uow.Transaction, cancellationToken);
        var pack = packs.Find(company?.CountryCode);

        // Every row is checked before anything is written
        var rows = (request.Rows ?? [])
            .Select(r => FieldPolicyRules.NormalizeOverride(r.FieldKey, r.State, r.DefaultValue, r.LabelKey, pack))
            .ToList();
        if (rows.GroupBy(r => r.FieldKey, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } duplicate)
        {
            throw new DomainException(FieldPolicyRules.InvalidPolicyCode, duplicate.Key);
        }

        var removed = rows.Where(r => (r.Entry.State, r.Entry.DefaultValue, r.Entry.LabelKey) is (null, null, null)).Select(r => r.FieldKey).ToList();
        await overrides.DeleteAsync(removed, uow.Transaction, cancellationToken);
        foreach (var (key, entry) in rows.Where(r => !removed.Contains(r.FieldKey)))
        {
            await overrides.UpsertAsync(key, entry, updatedBy, uow.Transaction, cancellationToken);
        }

        var after = await overrides.FindAllAsync(uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return after;
    }

    private static Dictionary<string, string?> ValidateExt(
        FieldPolicy policy, IReadOnlyDictionary<string, string?> input, IReadOnlyDictionary<string, string?>? stored)
    {
        if (input.Keys.Select(k => FieldCatalog.ExtKey(policy.ScreenCode, k)).FirstOrDefault(k => !policy.IsDeclared(k)) is { } undeclared)
        {
            throw new DomainException(FieldPolicyRules.UndeclaredExtCode, undeclared);
        }
        var prefix = FieldCatalog.ExtKey(policy.ScreenCode, "");
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var field in policy.ExtFields)
        {
            var name = field.FieldKey[prefix.Length..];
            var value = FieldState.IgnoresInput(field.State)
                ? stored is null ? field.DefaultValue : stored.GetValueOrDefault(name)
                : input.GetValueOrDefault(name);
            if (field.State == FieldState.Required && FieldPolicyRules.IsEmpty(value))
            {
                throw new DomainException(FieldPolicyRules.RequiredEmptyCode, field.LabelKey);
            }
            if (value is not null)
            {
                result[name] = value;
            }
        }
        return result;
    }

    private static IReadOnlyDictionary<string, string?> ExtOf(PropertyInfo ext, object dto) =>
        (IReadOnlyDictionary<string, string?>?)ext.GetValue(dto) ?? new Dictionary<string, string?>();

    /// <summary>The policy default as the property's type; without one, the type's default.</summary>
    private static object? DefaultOf(Type type, string? text)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        if (text is null)
        {
            return type.IsValueType && Nullable.GetUnderlyingType(type) is null ? Activator.CreateInstance(type) : null;
        }
        return target == typeof(string) ? text : TypeDescriptor.GetConverter(target).ConvertFromInvariantString(text);
    }

    private static T Clone<T>(T dto) where T : class => (T)MemberwiseCloneMethod.Invoke(dto, null)!;

    private static (PropertyInfo[] Fields, PropertyInfo? Ext) PropertiesOf(Type type) => Properties.GetOrAdd(type, t =>
    {
        var writable = t.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.CanRead && p.SetMethod is { IsPublic: true } && p.GetIndexParameters().Length == 0)
            .ToList();
        var ext = writable.FirstOrDefault(p => p.Name == ExtAttrsProperty && p.PropertyType == typeof(IReadOnlyDictionary<string, string?>));
        return ([.. writable.Where(p => p != ext)], ext);
    });
}
