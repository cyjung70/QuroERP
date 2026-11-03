using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using QuroErp.Contracts.Bc.FieldPolicies;
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

    [Inject]
    private IServiceProvider ScreenServices { get; set; } = default!;

    [CascadingParameter]
    private Task<AuthenticationState>? ScreenAuthentication { get; set; }

    /// <summary>
    /// The screen code of the field policies (field-policy.md 3), e.g. <c>Sd.Customer</c>. The screen reads them once when it
    /// opens and its inputs and grid columns with a field key follow them. Without it the screen has no field policies.
    /// </summary>
    [Parameter]
    public string? PolicyScreen { get; set; }

    /// <summary>Reads the policies instead of the API, e.g. a sample's own data. It takes the place of <see cref="PolicyScreen"/>.</summary>
    [Parameter]
    public Func<Task<IReadOnlyList<FieldPolicyDto>>>? LoadPolicies { get; set; }

    /// <summary>The screen's field policies, cascaded to its inputs and grids. Empty until they are read.</summary>
    protected QuroFieldPolicy Policies { get; } = new();

    /// <summary>
    /// The command bar shows "show all fields": the screen has field policies and the user has Full Access on the localization
    /// menu (BC-BR-435), the admin role until the menu policy is applied.
    /// </summary>
    protected bool CanShowAllFields { get; private set; }

    private bool _policiesWatched;

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

    /// <summary>
    /// Reads the field policies once (the circuit caches them by screen). When they cannot be read the message says so and the
    /// fields stay as the specification draws them; the server still checks the policies on save.
    /// </summary>
    protected async Task LoadPoliciesAsync()
    {
        if (PolicyScreen is null && LoadPolicies is null)
        {
            return;
        }
        if (!_policiesWatched)
        {
            _policiesWatched = true;
            Policies.Changed += () => _ = InvokeAsync(() =>
            {
                OnPoliciesChanged();
                StateHasChanged();
            });
        }
        CanShowAllFields = ScreenAuthentication is not null && (await ScreenAuthentication).User.IsInRole("admin");
        try
        {
            Policies.Load(LoadPolicies is not null
                ? await LoadPolicies()
                : await ScreenServices.GetRequiredService<FieldPolicyCache>().GetAsync(PolicyScreen!, CancellationToken.None));
        }
        catch (ApiProblemException e)
        {
            Error = Messages.Get(e.Problem);
        }
        catch (HttpRequestException)
        {
            Error = Messages.Get(UnavailableCode);
        }
    }

    /// <summary>Reads the field policies again, e.g. after a sample switched its country pack. The open record stays.</summary>
    public async Task ReloadPoliciesAsync()
    {
        await LoadPoliciesAsync();
        StateHasChanged();
    }

    /// <summary>The policies arrived or "show all fields" changed; a screen type applies them to its own grids here.</summary>
    protected virtual void OnPoliciesChanged()
    {
    }

    /// <summary>The command bar's "show all fields" toggle. It changes nothing that is saved (BC-BR-435).</summary>
    protected void ToggleShowAllFields() => Policies.SetShowAllFields(CanShowAllFields && !Policies.ShowAllFields);

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
