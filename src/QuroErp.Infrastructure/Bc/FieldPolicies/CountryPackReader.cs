using System.Reflection;
using QuroErp.Application.Bc.FieldPolicies;
using QuroErp.Domain.Bc.FieldPolicies;
using YamlDotNet.RepresentationModel;

namespace QuroErp.Infrastructure.Bc.FieldPolicies;

/// <summary>
/// Reads the country packs built into this assembly (countrypacks/{country}/*.yaml, field-policy.md "국가팩"). A pack that cannot
/// be read stops the start-up: the packs are checked by FieldPolicyLintTests before they ship.
/// </summary>
public static class CountryPackReader
{
    public const string FieldPolicyFile = "field-policy.yaml";
    public const string DefaultsFile = "defaults.yaml";

    private const string ResourcePrefix = "countrypacks/";
    private const string LabelsPrefix = "labels.";

    /// <summary>Every embedded pack in country order.</summary>
    public static IReadOnlyList<CountryPack> ReadEmbedded()
    {
        var assembly = typeof(CountryPackReader).Assembly;
        var files = assembly.GetManifestResourceNames()
            .Select(name => (Resource: name, Path: name.Replace('\\', '/')))
            .Where(f => f.Path.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .Select(f => (f.Resource, Parts: f.Path[ResourcePrefix.Length..].Split('/')))
            .Where(f => f.Parts.Length == 2)
            .GroupBy(f => f.Parts[0], StringComparer.Ordinal);
        return [.. files.OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => Parse(g.Key, g.ToDictionary(f => f.Parts[1], f => Text(assembly, f.Resource), StringComparer.Ordinal)))];
    }

    /// <summary>Builds a pack from its files: file name → YAML text. Missing files are empty.</summary>
    /// <exception cref="InvalidDataException">A file is not a mapping of the expected shape.</exception>
    public static CountryPack Parse(string countryCode, IReadOnlyDictionary<string, string> files)
    {
        var policies = Mapping(countryCode, FieldPolicyFile, files)
            .ToDictionary(e => e.Key, e => Entry(countryCode, e.Key, e.Value), StringComparer.Ordinal);
        var defaults = Mapping(countryCode, DefaultsFile, files)
            .ToDictionary(e => e.Key, e => Scalar(countryCode, DefaultsFile, e.Key, e.Value), StringComparer.Ordinal);
        var labels = files.Keys
            .Where(f => f.StartsWith(LabelsPrefix, StringComparison.Ordinal) && f.EndsWith(".yaml", StringComparison.Ordinal))
            .ToDictionary(
                f => f[LabelsPrefix.Length..^".yaml".Length],
                f => (IReadOnlyDictionary<string, string>)Mapping(countryCode, f, files)
                    .ToDictionary(e => e.Key, e => Scalar(countryCode, f, e.Key, e.Value), StringComparer.Ordinal),
                StringComparer.Ordinal);
        return new CountryPack(countryCode, policies, defaults, labels);
    }

    private static FieldPolicyEntry Entry(string countryCode, string key, YamlNode node)
    {
        if (node is YamlScalarNode)
        {
            return new FieldPolicyEntry(Scalar(countryCode, FieldPolicyFile, key, node), null, null);
        }
        if (node is not YamlMappingNode mapping)
        {
            throw Invalid(countryCode, FieldPolicyFile, key, "a state or a mapping of state, default and label");
        }
        string? state = null, defaultValue = null, label = null;
        foreach (var (name, value) in mapping.Children.Select(c => (Name: ((YamlScalarNode)c.Key).Value, c.Value)))
        {
            var text = Scalar(countryCode, FieldPolicyFile, $"{key}.{name}", value);
            switch (name)
            {
                case "state": state = text; break;
                case "default": defaultValue = text; break;
                case "label": label = text; break;
                default: throw Invalid(countryCode, FieldPolicyFile, key, "only state, default and label");
            }
        }
        return new FieldPolicyEntry(state, defaultValue, label);
    }

    private static IEnumerable<KeyValuePair<string, YamlNode>> Mapping(string countryCode, string file, IReadOnlyDictionary<string, string> files)
    {
        if (!files.TryGetValue(file, out var text))
        {
            return [];
        }
        var stream = new YamlStream();
        stream.Load(new StringReader(text));
        if (stream.Documents.Count == 0)
        {
            return [];
        }
        if (stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            throw Invalid(countryCode, file, "(root)", "a mapping");
        }
        return root.Children.Select(c => KeyValuePair.Create(
            c.Key is YamlScalarNode { Value: { } key } ? key : throw Invalid(countryCode, file, "(key)", "a text key"), c.Value));
    }

    private static string Scalar(string countryCode, string file, string key, YamlNode node) =>
        node is YamlScalarNode { Value: { Length: > 0 } value } ? value : throw Invalid(countryCode, file, key, "a text value");

    private static InvalidDataException Invalid(string countryCode, string file, string key, string expected) =>
        new($"countrypacks/{countryCode}/{file} {key}: expected {expected}.");

    private static string Text(Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

/// <summary>The embedded packs, read once.</summary>
internal sealed class EmbeddedCountryPackProvider : ICountryPackProvider
{
    private readonly Lazy<IReadOnlyList<CountryPack>> _packs = new(CountryPackReader.ReadEmbedded);

    public IReadOnlyList<CountryPack> All => _packs.Value;

    public CountryPack? Find(string? countryCode) => All.FirstOrDefault(p => p.CountryCode == countryCode);
}
