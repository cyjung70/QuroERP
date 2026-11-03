using System.Text.Json;

namespace QuroErp.Web.Components.Screens;

/// <summary>Value snapshots for change detection.</summary>
internal static class Snapshot
{
    public static string Of<T>(T value) => JsonSerializer.Serialize(value);

    public static T Restore<T>(string snapshot) =>
        JsonSerializer.Deserialize<T>(snapshot) ?? throw new InvalidOperationException("Empty snapshot.");
}
