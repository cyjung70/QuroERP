using QuroErp.Web.Common;

namespace QuroErp.Web.Tests.Screens;

/// <summary>Grants or denies write access whatever the role is, and remembers the screen paths it was asked about.</summary>
public sealed class FakeScreenAccess(bool canWrite) : IScreenAccess
{
    public List<string> Asked { get; } = [];

    public Task<bool> CanWriteAsync(string screenPath)
    {
        Asked.Add(screenPath);
        return Task.FromResult(canWrite);
    }
}
