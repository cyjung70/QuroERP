using Quro.Grid.Definition;

namespace QuroErp.Web.Common;

/// <summary>
/// The screen definitions (<c>Definitions/**/*.screen.toon</c>, compiled into this assembly), loaded and checked once
/// (qurogrid-integration.md 5). The app stops at startup when a definition has an error (QuroGrid consumer contract 6).
/// </summary>
public sealed class ScreenDefinitions
{
    private const string Prefix = "Definitions/";

    private static readonly Lazy<ScreenDefinitions> Loaded = new(() => Load(Read(), Catalog));

    private readonly Dictionary<string, ScreenDef> _screens;

    private ScreenDefinitions(Dictionary<string, ScreenDef> screens) => _screens = screens;

    /// <summary>The definitions of this assembly. Throws on the first use when a definition has an error.</summary>
    public static ScreenDefinitions Shared => Loaded.Value;

    /// <summary>
    /// The catalog of the running app. Code majors are those the definitions use: the web app does not read the code tables, and
    /// the definition tests check them against the code seed.
    /// </summary>
    private static ErpDefinitionCatalog Catalog(IReadOnlyCollection<ScreenDef> screens) => new(
        ErpDefinitionCatalog.SharedResourceKeys(),
        CodeMajorsOf(screens),
        new HashSet<string>(),
        new HashSet<string>(),
        ErpDefinitionCatalog.KnownPermissions);

    public ScreenDef Get(string id) =>
        _screens.TryGetValue(id, out var screen) ? screen : throw new KeyNotFoundException($"No screen definition {id}.");

    /// <summary>
    /// Loads the given files (path relative to <c>Definitions/</c>, text) and checks them with the catalog made from the loaded
    /// screens. Throws with every error, one <c>file:line: V-xx message</c> per line.
    /// </summary>
    public static ScreenDefinitions Load(
        IEnumerable<(string Path, string Text)> files, Func<IReadOnlyCollection<ScreenDef>, IQuroDefinitionCatalog> catalog)
    {
        var errors = new List<DefinitionError>();
        var screens = new Dictionary<string, ScreenDef>(StringComparer.Ordinal);
        foreach (var (path, text) in files.Where(f => f.Path.EndsWith(".screen.toon", StringComparison.Ordinal)))
        {
            var result = ScreenDefLoader.Load(path, text);
            errors.AddRange(result.Errors);
            if (result.Value is { } screen)
            {
                screens[screen.Id] = screen;
            }
        }
        if (errors.Count == 0)
        {
            var screenCatalog = catalog(screens.Values);
            var validator = new ScreenDefValidator(screenCatalog);
            errors.AddRange(screens.Values.SelectMany(validator.Validate).Where(e => !IsKeyShape(e)));
            errors.AddRange(screens.Values.SelectMany(s => MissingKeys(s, screenCatalog.ResourceKeys)));
        }
        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Screen definitions have errors:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
        }
        return new ScreenDefinitions(screens);
    }

    // QuroGrid wants resource keys that start in lower case (V-21); QuroERP keys are PascalCase (i18n.md). The shape error is
    // skipped and every key is still checked against the resource files below (TODO(QuroGrid-request))
    private static bool IsKeyShape(DefinitionError error) =>
        error.Code == "V-21" && error.Message.EndsWith("is not a resource key (for example module.doc.name)", StringComparison.Ordinal);

    // The keys the validator checks: title, field and column labels, validation messages
    private static IEnumerable<DefinitionError> MissingKeys(ScreenDef screen, IReadOnlySet<string> keys)
    {
        var used = new List<(string? Key, int Line, string What)> { (screen.Title, screen.Source.Line, "title") };
        used.AddRange(screen.Form?.Fields.Where(f => f.Type != FieldType.Hidden || f.Label is not null)
            .Select(f => (f.Label, f.Source.Line, $"label of '{f.Id}'")) ?? []);
        foreach (var grid in screen.Grids)
        {
            used.AddRange(grid.Columns.Select(c => ((string?)c.Label, c.Source.Line, $"label of '{c.Id}'")));
            used.AddRange(grid.Validate.Select(v => ((string?)v.Msg, v.Source.Line, $"message of '{v.Target}'")));
        }
        return used.Where(u => u.Key is null || !keys.Contains(u.Key))
            .Select(u => new DefinitionError("V-21", screen.Source.File, u.Line, $"{u.What} '{u.Key}' is not in the resource files"));
    }

    /// <summary>Majors referenced as <c>code:{major}</c> by grid columns and form fields.</summary>
    public static IReadOnlySet<string> CodeMajorsOf(IEnumerable<ScreenDef> screens) => screens
        .SelectMany(s => s.Grids.SelectMany(g => g.Columns.Select(c => c.DataSource))
            .Concat(s.Form?.Fields.Select(f => f.DataSource) ?? []))
        .Where(source => source?.StartsWith("code:", StringComparison.Ordinal) == true)
        .Select(source => source!["code:".Length..])
        .ToHashSet(StringComparer.Ordinal);

    // The .toon files are embedded with their path under Definitions/ as the resource name (QuroErp.Web.csproj)
    private static IEnumerable<(string Path, string Text)> Read()
    {
        var assembly = typeof(ScreenDefinitions).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            yield return (name[Prefix.Length..], reader.ReadToEnd());
        }
    }
}
