using System.Collections;
using System.Globalization;
using System.Resources;
using Quro.Grid.Definition;

namespace QuroErp.Web.Common;

/// <summary>
/// What QuroERP has registered, for the catalog check of the screen definitions (qurogrid-integration.md 5,
/// QuroGrid consumer contract 6).
/// </summary>
public sealed class ErpDefinitionCatalog(
    IReadOnlySet<string> resourceKeys,
    IReadOnlySet<string> codeMajors,
    IReadOnlySet<string> lookupIds,
    IReadOnlySet<string> handlers,
    IReadOnlySet<string> permissions) : IQuroDefinitionCatalog
{
    /// <summary>
    /// Permission ids a definition may use in <c>perm</c> and <c>readOnlyFor</c>: <c>{MENU}.Read</c> and <c>{MENU}.Write</c> of the
    /// screens that have a definition. The naming waits for Harness to confirm it.
    /// </summary>
    public static IReadOnlySet<string> KnownPermissions { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "SAMPLES.Read",
        "SAMPLES.Write",
        "BC.Read",
        "BC.Write",
    };

    public IReadOnlySet<string> ResourceKeys { get; } = resourceKeys;

    public IReadOnlySet<string> CodeMajors { get; } = codeMajors;

    public IReadOnlySet<string> LookupIds { get; } = lookupIds;

    public IReadOnlySet<string> Handlers { get; } = handlers;

    public IReadOnlySet<string> Permissions { get; } = permissions;

    /// <summary>The keys of the default (ko) resource file, which has every key (i18n.md).</summary>
    public static IReadOnlySet<string> SharedResourceKeys()
    {
        var resources = new ResourceManager("QuroErp.Web.Resources.SharedResource", typeof(SharedResource).Assembly);
        var set = resources.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false)
            ?? throw new InvalidOperationException("SharedResource has no default resources.");
        return set.Cast<DictionaryEntry>().Select(e => (string)e.Key).ToHashSet(StringComparer.Ordinal);
    }
}
