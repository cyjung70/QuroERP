using QuroErp.Web.Components.Screens;

namespace QuroErp.Web.Components.Pages.Samples;

/// <summary>
/// The sampleItem lookup (<c>Definitions/lookups/samples/sampleItem.lookup.toon</c>) over the sample store. Development only;
/// a real lookup calls a feature's Contracts API client.
/// </summary>
public static class SampleLookups
{
    /// <summary>Items whose code or name contains the keyword, or the items of the exact codes.</summary>
    public static IReadOnlyList<LookupRow> Items(SampleStore store, LookupQuery query)
    {
        var items = query.Codes is { } codes
            ? codes.Select(store.FindItem).OfType<SampleItem>()
            : store.Items().Where(i => string.IsNullOrWhiteSpace(query.Keyword)
                                       || (i.Code?.Contains(query.Keyword, StringComparison.OrdinalIgnoreCase) ?? false)
                                       || (i.Name?.Contains(query.Keyword, StringComparison.OrdinalIgnoreCase) ?? false));
        return [.. items.Select(i => LookupRow.Of(("code", i.Code), ("name", i.Name)))];
    }
}
