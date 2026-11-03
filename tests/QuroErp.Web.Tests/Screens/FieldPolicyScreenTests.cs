using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using QuroErp.Contracts.Bc.FieldPolicies;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Web.Auth;
using QuroErp.Web.Bc.Localization;
using QuroErp.Web.Components.Pages.Samples;
using QuroErp.Web.Components.Screens;

namespace QuroErp.Web.Tests.Screens;

/// <summary>
/// Field policies on the inputs, grid columns and command bar (TASK-CORE-048, field-policy.md 3, BC-BR-432, BC-BR-434,
/// BC-BR-435). The S sample switches between its own US and KR policies.
/// </summary>
public class FieldPolicyScreenTests : ScreenContext
{
    private readonly BunitAuthorizationContext _auth;
    private readonly PolicyApi _api = new();

    public FieldPolicyScreenTests()
    {
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton<AccessTokenStore>();
        Services.AddScoped<AccessTokenProvider>();
        Services.AddHttpClient<AuthApiClient>(http => http.BaseAddress = new Uri("http://api.test"));
        Services.AddHttpClient<FieldPolicyApiClient>(http => http.BaseAddress = new Uri("http://api.test"))
            .ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddScoped<FieldPolicyCache>();
        _auth = AddAuthorization();
    }

    [Fact, Trait("Id", "BC-UT-434"), Trait("Rule", "BC-BR-434")]
    public void HiddenField_IsNotDrawn()
    {
        var page = RenderSample("US");

        page.FindAll("[data-testid=single-header-remarks]").Should().BeEmpty();
        page.FindAll("[data-testid=single-header-name]").Should().ContainSingle();
    }

    [Fact, Trait("Id", "BC-UT-432"), Trait("Rule", "BC-BR-432")]
    public void RequiredPolicy_ShowsTheMarker_AndTheLabelComesFromThePolicy()
    {
        var page = RenderSample("US");

        var field = page.Find("[data-testid=single-header-valid-from]");
        field.ClassList.Should().Contain("quro-field-required");
        field.QuerySelector("label")!.TextContent.Should().Be(L("Samples.ValidFrom") + "*");
    }

    [Fact, Trait("Id", "BC-UT-432"), Trait("Rule", "BC-BR-432")]
    public void RequiredPolicy_OnABoundField_IsAValidateError_AndBlocksSave()
    {
        var page = RenderSample("US");
        page.Find("[data-testid=single-header-code] input").Change("C300");
        page.Find("[data-testid=single-header-name] input").Change("Cherry");

        Form(page).DataSet.Validate().Errors.Select(e => e.Field).Should().Contain("ValidFrom");
        page.Find("[data-testid=single-toolbar-save]").Click();

        page.Find("[data-testid=single-message]").TextContent.Should().Contain(L("Samples.ValidFrom"));
        Store.FindItem("C300").Should().BeNull();
    }

    [Fact, Trait("Id", "BC-UT-434"), Trait("Rule", "BC-BR-434")]
    public void HiddenPolicy_TakesTheFieldOutOfValidate()
    {
        var page = Render<QuroSingleForm<RequiredModel>>(p => p
            .Add(x => x.Title, "T")
            .Add(x => x.TestId, "t")
            .Add(x => x.LoadPolicies, () => Policies(("Samples.Remarks", FieldPolicyStates.Hidden)))
            .Add(x => x.Load, () => Task.FromResult<RequiredModel?>(null))
            .Add(x => x.Create, () => new RequiredModel())
            .Add(x => x.Save, (m, _) => Task.FromResult(m))
            .Add(x => x.Fields, (RenderFragment<RequiredModel>)(_ => b =>
            {
                b.OpenComponent<QuroText>(0);
                b.AddComponentParameter(1, nameof(QuroText.Policy), "Samples.Remarks");
                b.AddComponentParameter(2, nameof(QuroText.Field), nameof(RequiredModel.Remarks));
                b.AddComponentParameter(3, nameof(QuroText.TestId), "t-header-remarks");
                b.CloseComponent();
            })));

        page.FindAll("[data-testid=t-header-remarks]").Should().BeEmpty();
        page.Instance.DataSet.Validate().IsValid.Should().BeTrue(
            "the policy hides the [Required] Remarks; the server checks it");
    }

    [Fact, Trait("Id", "BC-UT-434"), Trait("Rule", "BC-BR-434")]
    public void ReadOnlyPolicy_LocksTheField()
    {
        var page = RenderSample("KR");

        page.Field("single-header-qty").IsReadOnly.Should().BeTrue();
        page.Find("[data-testid=single-header-qty]").Closest(".quro-field")!.ClassList.Should().Contain("quro-field-readonly");
        page.Field("single-header-name").IsReadOnly.Should().BeFalse();
    }

    [Fact, Trait("Id", "BC-UT-434")]
    public void SwitchingTheCountryPack_ChangesTheFields_AndKeepsTheRecord()
    {
        var page = RenderSample("US");
        page.Find("[data-testid=single-search-code]").Change("A100");
        page.Find("[data-testid=single-toolbar-search]").Click();

        page.Find("[data-testid=single-search-country]").Change("KR");

        page.FindAll("[data-testid=single-header-remarks]").Should().ContainSingle();
        page.Field("single-header-qty").IsReadOnly.Should().BeTrue();
        page.Find("[data-testid=single-doc-no]").TextContent.Should().Be("A100");
    }

    [Fact, Trait("Id", "BC-UT-435"), Trait("Rule", "BC-BR-435")]
    public void ShowAllFields_IsOnlyForAdmins()
    {
        SignIn("user");
        RenderSample("US").FindAll("[data-testid=single-toolbar-show-all-fields]").Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UT-435"), Trait("Rule", "BC-BR-435")]
    public void ShowAllFields_ShowsHiddenFieldsReadOnly_WithTheMarker_AndSavesTheSame()
    {
        SignIn("admin");
        var page = RenderSample("US");
        page.Find("[data-testid=single-search-code]").Change("A100");
        page.Find("[data-testid=single-toolbar-search]").Click();
        var remarks = Store.FindItem("A100")!.Remarks;

        page.Find("[data-testid=single-toolbar-show-all-fields]").Click();

        page.Find("[data-testid=single-toolbar-show-all-fields]").GetAttribute("aria-pressed").Should().Be("true");
        var field = page.Find("[data-testid=single-header-remarks]");
        field.HasAttribute("readonly").Should().BeTrue();
        field.Closest(".quro-field")!.ClassList.Should().Contain("quro-field-policy-hidden");
        field.Closest(".quro-field")!.QuerySelector(".quro-policy-hidden-mark")!.TextContent.Should().Be(L("Common.HiddenByPolicy"));
        Form(page).IsDirty.Should().BeFalse("showing a field changes nothing");

        page.Find("[data-testid=single-header-name] input").Change("Apricot 2");
        page.Find("[data-testid=single-toolbar-save]").Click();
        Store.FindItem("A100")!.Remarks.Should().Be(remarks);
    }

    [Fact, Trait("Id", "BC-UT-431")]
    public void PolicyScreen_IsReadOnceForTheCircuit()
    {
        SignIn("admin");
        _api.Fields = [new("Sd.Customer.State", FieldPolicyStates.Hidden, null, "Sd.Customer.State", "CountryPack")];

        var first = RenderCustomer();
        var second = RenderCustomer();

        first.WaitForAssertion(() => first.FindAll("[data-testid=t-header-state]").Should().BeEmpty());
        second.WaitForAssertion(() => second.FindAll("[data-testid=t-header-zip]").Should().ContainSingle());
        second.FindAll("[data-testid=t-header-state]").Should().BeEmpty();
        _api.Calls.Should().Be(1);
    }

    [Fact, Trait("Id", "BC-UT-431")]
    public void PolicyScreen_ThatCannotBeRead_ShowsTheMessage_AndKeepsTheFields()
    {
        SignIn("admin");
        _api.Status = HttpStatusCode.ServiceUnavailable;

        var page = RenderCustomer();

        page.WaitForAssertion(() => page.Find("[data-testid=t-message]").TextContent.Should().NotBeEmpty());
        page.FindAll("[data-testid=t-header-state]").Should().ContainSingle();
    }

    [Theory, Trait("Id", "BC-UT-434"), Trait("Rule", "BC-BR-434")]
    [InlineData(FieldPolicyStates.Hidden, false)]
    [InlineData(FieldPolicyStates.ReadOnly, true)]
    public void LineGridColumn_FollowsThePolicyByItsLabelKey(string state, bool shown)
    {
        SignIn("admin");
        var page = RenderOrder(state);
        var lines = page.DataGrid<SampleLine, QuroHeaderDetail<SampleOrder, SampleLine>>("t-line");
        page.WaitForAssertion(() => lines.Rows.Should().HaveCount(2));

        lines.Headers.Select(h => h.Field).Contains("qty").Should().Be(shown);
        if (shown)
        {
            lines.IsReadOnly(1, "qty").Should().BeTrue();
        }
        else
        {
            page.Find("[data-testid=t-toolbar-show-all-fields]").Click();
            page.WaitForAssertion(() => lines.Headers.Select(h => h.Field).Should().Contain("qty"));
            lines.IsReadOnly(1, "qty").Should().BeTrue();
        }
    }

    [Fact, Trait("Id", "BC-UT-434"), Trait("Rule", "BC-BR-434")]
    public void QuroColumn_WithAHiddenPolicy_IsLeftOut_UntilShowAllFields()
    {
        var policy = new QuroFieldPolicy();
        policy.Load([new("Samples.Remarks", FieldPolicyStates.Hidden, null, "Samples.Remarks", "CountryPack")]);
        var set = new QuroColumnSet<SampleItem>();
        Render<CascadingValue<QuroFieldPolicy>>(p => p
            .Add(x => x.Value, policy)
            .Add(x => x.IsFixed, true)
            .AddChildContent<CascadingValue<QuroColumnSet<SampleItem>>>(c => c
                .Add(x => x.Value, set)
                .Add(x => x.IsFixed, true)
                .AddChildContent(b =>
                {
                    b.OpenComponent<QuroColumn<SampleItem>>(0);
                    b.AddComponentParameter(1, nameof(QuroColumn<SampleItem>.Title), "Name");
                    b.CloseComponent();
                    b.OpenComponent<QuroColumn<SampleItem>>(2);
                    b.AddComponentParameter(3, nameof(QuroColumn<SampleItem>.Title), "Remarks");
                    b.AddComponentParameter(4, nameof(QuroColumn<SampleItem>.Policy), "Samples.Remarks");
                    b.CloseComponent();
                })));

        set.Items.Select(c => c.Title).Should().Equal("Name");
        policy.SetShowAllFields(true);
        set.Items.Select(c => c.Title).Should().Equal("Name", "Remarks");
    }

    [Fact, Trait("Id", "BC-UT-432"), Trait("Rule", "BC-BR-432")]
    public void StricterOfRequiredAndPolicy_Applies()
    {
        var policy = new QuroFieldPolicy();
        policy.Load([new("K.Optional", FieldPolicyStates.Optional, null, "K.Optional", "System")]);
        var page = Render<CascadingValue<QuroFieldPolicy>>(p => p
            .Add(x => x.Value, policy)
            .Add(x => x.IsFixed, true)
            .AddChildContent<QuroText>(t => t
                .Add(x => x.Policy, "K.Optional")
                .Add(x => x.Label, "Code")
                .Add(x => x.Required, true)
                .Add(x => x.TestId, "k")));

        page.Find("[data-testid=k]").Closest(".quro-field")!.ClassList.Should().Contain("quro-field-required",
            "the screen's Required stays when the policy is only OPTIONAL");
        page.Find("[data-testid=k]").Closest(".quro-field")!.QuerySelector("label")!.TextContent.Should().Be("Code*", "a given label wins");
    }

    private IRenderedComponent<SampleSingle> RenderSample(string country)
    {
        var page = Render<SampleSingle>();
        page.Find("[data-testid=single-search-country]").Change(country);
        return page;
    }

    private IRenderedComponent<QuroSingleForm<SampleItem>> RenderCustomer() =>
        Render<QuroSingleForm<SampleItem>>(p => p
            .Add(x => x.Title, "T")
            .Add(x => x.TestId, "t")
            .Add(x => x.PolicyScreen, "Sd.Customer")
            .Add(x => x.Load, () => Task.FromResult<SampleItem?>(null))
            .Add(x => x.Create, () => new SampleItem())
            .Add(x => x.Save, (m, _) => Task.FromResult(m))
            .Add(x => x.Fields, (RenderFragment<SampleItem>)(_ => b =>
            {
                b.OpenComponent<QuroText>(0);
                b.AddComponentParameter(1, nameof(QuroText.Policy), "Sd.Customer.State");
                b.AddComponentParameter(2, nameof(QuroText.Field), nameof(SampleItem.Name));
                b.AddComponentParameter(3, nameof(QuroText.TestId), "t-header-state");
                b.CloseComponent();
                b.OpenComponent<QuroText>(4);
                b.AddComponentParameter(5, nameof(QuroText.Policy), "Sd.Customer.ZipCode");
                b.AddComponentParameter(6, nameof(QuroText.Field), nameof(SampleItem.Remarks));
                b.AddComponentParameter(7, nameof(QuroText.TestId), "t-header-zip");
                b.CloseComponent();
            })));

    private IRenderedComponent<QuroHeaderDetail<SampleOrder, SampleLine>> RenderOrder(string qtyState) =>
        Render<QuroHeaderDetail<SampleOrder, SampleLine>>(p => p
            .Add(x => x.Title, "T")
            .Add(x => x.TestId, "t")
            .Add(x => x.Definition, "samples.order.entry")
            .Add(x => x.LoadPolicies, () => Policies(("Samples.Qty", qtyState)))
            .Add(x => x.HeaderFields, (RenderFragment<SampleOrder>)(_ => _ => { }))
            .Add(x => x.FindColumns, (RenderFragment)(_ => { }))
            .Add(x => x.Find, _ => Task.FromResult<IReadOnlyList<SampleOrder>>([]))
            .Add(x => x.Load, _ => Task.FromResult<HeaderLines<SampleOrder, SampleLine>?>(null))
            .Add(x => x.Initial, () => Task.FromResult(Store.FindOrder("SO261000001") is { } o
                ? new HeaderLines<SampleOrder, SampleLine>(o.Order, o.Lines) : null))
            .Add(x => x.Create, () => new SampleOrder())
            .Add(x => x.CreateLine, _ => new SampleLine())
            .Add(x => x.Save, (o, _, _) => Task.FromResult(new HeaderLines<SampleOrder, SampleLine>(o, []))));

    private static Task<IReadOnlyList<FieldPolicyDto>> Policies(params (string Key, string State)[] fields) =>
        Task.FromResult<IReadOnlyList<FieldPolicyDto>>([.. fields.Select(f => new FieldPolicyDto(f.Key, f.State, null, f.Key, "CountryPack"))]);

    private void SignIn(string role)
    {
        var store = Services.GetRequiredService<AccessTokenStore>();
        var sessionId = store.Add(new LoginResponse($"token-{role}", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", role, false, "ko"));
        _auth.SetAuthorized("kim");
        _auth.SetRoles(role);
        _auth.SetClaims(new Claim(WebAuthClaims.SessionId, sessionId));
    }

    private string L(string key) =>
        Services.GetRequiredService<Microsoft.Extensions.Localization.IStringLocalizer<SharedResource>>()[key].Value;

    private static QuroSingleForm<SampleItem> Form(IRenderedComponent<SampleSingle> page) =>
        page.FindComponent<QuroSingleForm<SampleItem>>().Instance;

    /// <summary>A record whose remarks the DTO requires.</summary>
    public sealed class RequiredModel
    {
        [System.ComponentModel.DataAnnotations.Required]
        public string? Remarks { get; set; }
    }

    /// <summary>GET api/bc/field-policies: counts the calls and answers with <see cref="Fields"/> or <see cref="Status"/>.</summary>
    private sealed class PolicyApi : HttpMessageHandler
    {
        public List<FieldPolicyDto> Fields { get; set; } = [];

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Status == HttpStatusCode.OK
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Fields) }
                : new HttpResponseMessage(Status));
        }
    }
}
