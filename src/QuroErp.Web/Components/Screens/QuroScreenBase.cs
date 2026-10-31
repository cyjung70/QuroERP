using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using QuroErp.Web.Common;

namespace QuroErp.Web.Components.Screens;

/// <summary>Asks the user to confirm (unsaved changes, delete).</summary>
public interface IQuroConfirm
{
    Task<bool> ConfirmAsync(string message);
}

/// <summary>Browser confirm dialog.</summary>
public sealed class JsQuroConfirm(IJSRuntime js) : IQuroConfirm
{
    public async Task<bool> ConfirmAsync(string message) => await js.InvokeAsync<bool>("confirm", message);
}

/// <summary>
/// Common behaviour of the four screen type components (screens.md "공통 동작"): busy state, messages from
/// ProblemDetails codes, the unsaved-changes confirmation and the client-side required check.
/// </summary>
public abstract class QuroScreenBase : ComponentBase
{
    /// <summary>Required input is empty. Arg: field label.</summary>
    public const string RequiredCode = "Common-E-005";

    private const string UnavailableCode = "BC-E-003";

    [Inject]
    protected IQuroConfirm Confirm { get; set; } = default!;

    [Inject]
    protected ProblemMessages Messages { get; set; } = default!;

    [Inject]
    protected IStringLocalizer<SharedResource> L { get; set; } = default!;

    /// <summary>Translated screen title.</summary>
    [Parameter, EditorRequired]
    public string Title { get; set; } = "";

    /// <summary>
    /// Menu path above the title. S, M and MM pass the menu group (<c>Menu.&lt;group&gt;</c>) and the frame adds
    /// " › title" (screen-templates.md 0); SM shows it as given, e.g. "MM > Purchasing" (entry-frame.md 1).
    /// </summary>
    [Parameter]
    public string? Path { get; set; }

    /// <summary>Search conditions (top of the screen). Enter runs the search.</summary>
    [Parameter]
    public RenderFragment? SearchContent { get; set; }

    /// <summary>Prefix of the <c>data-testid</c> values of this screen and its toolbar buttons.</summary>
    [Parameter]
    public string? TestId { get; set; }

    protected bool Busy { get; private set; }

    protected string? Error { get; set; }

    protected string? Notice { get; set; }

    /// <summary>There are changes that are not saved.</summary>
    public abstract bool IsDirty { get; }

    /// <summary>True when nothing would be lost or the user agrees to discard the changes.</summary>
    protected async Task<bool> ConfirmDiscardAsync() => !IsDirty || await Confirm.ConfirmAsync(L["Common.ConfirmUnsaved"]);

    /// <summary>Navigation inside the app. Closing or reloading the browser is covered by NavigationLock.</summary>
    protected async Task GuardNavigationAsync(LocationChangingContext context)
    {
        if (!await ConfirmDiscardAsync())
        {
            context.PreventNavigation();
        }
    }

    /// <summary>Shows the first empty required input as Common-E-005.</summary>
    protected bool CheckRequired(params IQuroField?[] missing)
    {
        var first = missing.FirstOrDefault(f => f is not null);
        if (first is null)
        {
            return true;
        }
        Error = Messages.Get(RequiredCode, first.Label);
        Notice = null;
        return false;
    }

    // Shows API errors (ProblemDetails code) and connection failures as messages instead of breaking the circuit
    protected async Task RunAsync(Func<Task> action)
    {
        Busy = true;
        Error = null;
        Notice = null;
        try
        {
            await action();
        }
        catch (ApiProblemException e)
        {
            Error = Messages.Get(e.Problem);
        }
        catch (HttpRequestException)
        {
            Error = Messages.Get(UnavailableCode);
        }
        finally
        {
            Busy = false;
        }
    }

    protected string? Id(string part) => TestId is null ? null : $"{TestId}-{part}";
}
