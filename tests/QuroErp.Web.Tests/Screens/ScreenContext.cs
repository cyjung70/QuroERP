using System.Globalization;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Pages.Samples;
using QuroErp.Web.Components.Screens;

namespace QuroErp.Web.Tests.Screens;

/// <summary>Confirm dialog that records the questions and gives a fixed answer.</summary>
public sealed class FakeConfirm : IQuroConfirm
{
    public bool Answer { get; set; } = true;

    public List<string> Questions { get; } = [];

    public Task<bool> ConfirmAsync(string message)
    {
        Questions.Add(message);
        return Task.FromResult(Answer);
    }
}

/// <summary>bUnit context with the services of the screen type components, in ko.</summary>
public abstract class ScreenContext : BunitContext
{
    protected ScreenContext()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("ko");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddWebLocalization();
        Services.AddSingleton<IQuroConfirm>(Confirm);
        Services.AddSingleton(Store);
    }

    protected FakeConfirm Confirm { get; } = new();

    protected SampleStore Store { get; } = new();
}
