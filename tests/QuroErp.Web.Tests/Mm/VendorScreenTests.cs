using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuroErp.Contracts.Bc.FieldPolicies;
using QuroErp.Contracts.Bc.Localization;
using QuroErp.Contracts.Bc.Tax;
using QuroErp.Contracts.Common;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Contracts.Common.Codes;
using QuroErp.Contracts.Mm.Vendors;
using QuroErp.Web.Auth;
using QuroErp.Web.Bc.Localization;
using QuroErp.Web.Bc.Tax;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Pages.Mm;
using QuroErp.Web.Components.Screens;
using QuroErp.Web.Mm.Vendors;
using QuroErp.Web.Tests.Screens;

namespace QuroErp.Web.Tests.Mm;

/// <summary>Vendor master screen /mm/vendor (MM-UC-001, TASK-MM-002) against a fake vendor, code, tax and policy API.</summary>
public class VendorScreenTests : BunitContext
{
    private readonly FakeVendorApi _api = new();
    private readonly BunitAuthorizationContext _auth;

    public VendorScreenTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("ko");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddWebLocalization();
        Services.AddCompanyTime();
        Services.AddSingleton<IQuroConfirm>(new FakeConfirm());
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton<AccessTokenStore>();
        Services.AddScoped<AccessTokenProvider>();
        Services.AddScoped<FieldPolicyCache>();
        Services.AddHttpClient<AuthApiClient>(http => http.BaseAddress = new Uri("http://api.test"));
        Services.AddHttpClient<VendorApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<CodeApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<LocalizationApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<TaxCodeApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<FieldPolicyApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        _auth = AddAuthorization();
    }

    [Fact, Trait("Id", "MM-UT-851"), Trait("Rule", "MM-BR-851")]
    public void New_ShowsTheDefaults_AndBalanceIsReadOnly()
    {
        var page = RenderAs("admin");

        // The combo box keys its options in lower case
        page.Field("vendor-header-terms").Selected.Should().BeEquivalentTo("NET_30");
        page.Find("[data-testid=vendor-header-valid-flag-y]").HasAttribute("checked").Should().BeTrue();
        page.Field("vendor-header-country").Value.Should().Be("USA");
        page.Field("vendor-header-ship-country").Value.Should().Be("USA");
        page.Field("vendor-header-bal-amt").Value.Should().Be("0.00");
        page.Field("vendor-header-bal-amt").IsReadOnly.Should().BeTrue();
        page.Field("vendor-header-currency-code").Options.Should().Contain(("usd", "USD US Dollar"));
        page.Field("vendor-header-tax-code").Options.Should().Contain(("none", "NONE 비과세"));
    }

    [Fact, Trait("Id", "BC-UT-434"), Trait("Rule", "BC-BR-434")]
    public void UsPolicies_HideTheKrFields_AndSalesTax_HidesTheTaxPartyType()
    {
        var page = RenderAs("admin");

        page.FindAll("[data-testid=vendor-header-business-reg-no]").Should().BeEmpty();
        page.FindAll("[data-testid=vendor-header-ceo-name]").Should().BeEmpty();
        page.FindAll("[data-testid=vendor-header-tax-party-type]").Should().BeEmpty();
        page.Find("[data-testid=vendor-header-state]").Closest(".quro-field")!.ClassList.Should().Contain("quro-field-required");
        page.Find("[data-testid=vendor-header-v-name]").Closest(".quro-field")!.QuerySelector("label")!.TextContent
            .Should().Be("공급자 이름*", "the label comes from the policy's label key");
    }

    [Fact, Trait("Id", "MM-UT-872"), Trait("Rule", "MM-BR-872")]
    public void KrCompany_ShowsTheTaxPartyTypeAndTheKrFields_AndHidesTheState()
    {
        _api.Country = "KR";

        var page = RenderAs("admin");

        page.Field("vendor-header-tax-party-type").Options.Should().Contain([("business", "사업자"), ("individual", "개인")]);
        page.FindAll("[data-testid=vendor-header-business-reg-no]").Should().ContainSingle();
        page.FindAll("[data-testid=vendor-header-state]").Should().BeEmpty();
    }

    [Fact, Trait("Id", "MM-UC-001-T01")]
    public void SaveNew_PostsTheVendor_WithTheTaxCodeId_AndLocksTheName()
    {
        var page = RenderAs("admin");
        page.Field("vendor-header-v-name").Change("acme");
        page.Field("vendor-header-phone1").Change("555-0100");
        page.Field("vendor-header-state").Change("CA");
        page.Field("vendor-header-zip-code").Change("90001");
        page.Field("vendor-header-tax-code").Change("NONE");

        page.Find("[data-testid=vendor-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=vendor-message]").TextContent.Should().Be("저장했습니다."));
        var created = _api.Created.Should().ContainSingle().Subject;
        created.VName.Should().Be("ACME");
        created.Terms.Should().Be("NET_30");
        created.TaxCodeId.Should().Be(7);
        created.CurrencyCode.Should().BeNull("empty is the base currency");
        page.Field("vendor-header-v-name").IsReadOnly.Should().BeTrue();
        page.Field("vendor-search-v-name").Value.Should().Be("ACME");
    }

    [Fact, Trait("Id", "MM-UC-001-T02")]
    public void SaveWithoutPhone_ShowsTheRequiredMessage_AndDoesNotPost()
    {
        var page = RenderAs("admin");
        page.Field("vendor-header-v-name").Change("ACME");
        page.Field("vendor-header-state").Change("CA");
        page.Field("vendor-header-zip-code").Change("90001");

        page.Find("[data-testid=vendor-toolbar-save]").Click();

        page.Find("[data-testid=vendor-message]").TextContent.Should().Be("전화 1을(를) 입력하세요.");
        _api.Created.Should().BeEmpty();
    }

    [Fact, Trait("Id", "MM-UT-522"), Trait("Rule", "BC-BR-522")]
    public void SaveWithAWarning_ShowsItAfterSaved()
    {
        var page = RenderAs("admin");
        Search(page, "ACME");
        _api.Warnings = [new ApiWarning("BC-W-522", ["BETA"])];

        page.Find("[data-testid=vendor-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=vendor-message]").TextContent
            .Should().Be("저장했습니다. 같은 사업자등록번호를 쓰는 거래처가 있습니다: BETA. 저장은 했습니다."));
    }

    [Fact, Trait("Id", "MM-UC-001-T03")]
    public void UpdateAfterSomeoneElseSaved_Shows409Message()
    {
        var page = RenderAs("admin");
        Search(page, "ACME");
        _api.UpdateProblem = (HttpStatusCode.Conflict, """{"status":409,"code":"Common-E-004"}""");

        page.Find("[data-testid=vendor-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=vendor-message]").TextContent
            .Should().Be("다른 사용자가 먼저 수정했습니다. 다시 조회한 뒤 저장하세요."));
    }

    [Theory, Trait("Id", "MM-UC-001-T04"), Trait("Id", "MM-UT-859")]
    [InlineData("""{"status":422,"code":"MM-E-859","args":["Email","100"]}""", "이메일은(는) 100자 이하의 올바른 형식으로 입력하세요.")]
    [InlineData("""{"status":422,"code":"BC-E-522","args":["CeoName"]}""", "사업자 거래처는 대표자을(를) 입력해야 합니다.")]
    [InlineData("""{"status":422,"code":"Common-E-002","args":["TERMS","XX"]}""", "'XX'")]
    public void SaveRejectedByTheApi_ShowsTheMessage_WithFieldLabels(string problem, string message)
    {
        var page = RenderAs("admin");
        Search(page, "ACME");
        _api.UpdateProblem = (HttpStatusCode.UnprocessableEntity, problem);

        page.Find("[data-testid=vendor-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=vendor-message]").TextContent.Should().Contain(message));
    }

    [Fact, Trait("Id", "MM-UT-857"), Trait("Rule", "MM-BR-857")]
    public void DeletingAUsedVendor_ShowsMmE857_AndKeepsTheVendor()
    {
        var page = RenderAs("admin");
        Search(page, "ACME");
        _api.DeleteProblem = """{"status":422,"code":"MM-E-857","args":["ACME"]}""";

        page.Find("[data-testid=vendor-toolbar-delete]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=vendor-message]").TextContent
            .Should().Be("공급자 ACME은(는) 발주·입고·지급·구매 단가가 있어 지울 수 없습니다. 사용 안 함으로 바꾸세요."));
        page.Field("vendor-header-v-name").Value.Should().Be("ACME");
    }

    [Fact, Trait("Id", "MM-UT-858"), Trait("Rule", "MM-BR-858")]
    public void Search_ByTypedName_LoadsTheExactVendor_WithItsBalance()
    {
        var page = RenderAs("admin");

        Search(page, "acme");

        page.Field("vendor-header-phone1").Value.Should().Be("555-0100");
        page.Field("vendor-header-bal-amt").Value.Should().Be("70.50");
        page.Field("vendor-header-tax-code").Selected.Should().BeEquivalentTo("NONE");
    }

    [Fact, Trait("Id", "CORE-020-WT-02")]
    public void Next_LoadsTheFollowingVendor_AndTheSearchName()
    {
        var page = RenderAs("admin");
        Search(page, "ACME");

        page.Find("[data-testid=vendor-toolbar-next]").Click();

        page.WaitForAssertion(() => page.Field("vendor-header-v-name").Value.Should().Be("BETA"));
        page.Field("vendor-search-v-name").Value.Should().Be("BETA");
    }

    [Fact, Trait("Id", "MM-UC-001-T01")]
    public void Lookup_SelectedVendor_IsLoadedById()
    {
        var page = RenderAs("admin");

        page.Find("[data-testid=vendor-search-v-name-lookup]").Click();
        page.WaitForAssertion(() => page.LookupRows("vendor-vendor-lookup").Should().NotBeEmpty());
        page.PickLookupRow<VendorDto, Vendor>("vendor-vendor-lookup", 0);

        page.WaitForAssertion(() => page.Field("vendor-header-v-name").Value.Should().Be("ACME"));
        _api.Requests.Should().Contain(r => r.RequestUri!.AbsolutePath == "/api/mm/vendors/701");
    }

    [Fact, Trait("Id", "BC-UT-214")]
    public void UserWithoutFullAccess_SeesSearchAndMoveOnly_AndLockedInputs()
    {
        var page = RenderAs("user");
        Search(page, "ACME");

        page.FindAll("[role=toolbar] button[disabled]").Select(b => b.GetAttribute("data-testid"))
            .Should().Contain(["vendor-toolbar-new", "vendor-toolbar-save", "vendor-toolbar-delete"]);
        page.Field("vendor-header-phone1").IsReadOnly.Should().BeTrue();
        page.Field("vendor-header-terms").IsReadOnly.Should().BeTrue();
    }

    private IRenderedComponent<Vendor> RenderAs(string role)
    {
        var store = Services.GetRequiredService<AccessTokenStore>();
        var sessionId = store.Add(new LoginResponse($"token-{role}", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", role, false, "ko"));
        _auth.SetAuthorized("kim");
        _auth.SetRoles(role);
        _auth.SetClaims(new Claim(WebAuthClaims.SessionId, sessionId));
        var page = Render<Vendor>();
        page.WaitForElement("[data-testid=vendor-header-v-name]");
        page.WaitForAssertion(() => page.Field("vendor-header-terms").Options.Should().HaveCountGreaterThan(1));
        page.WaitForAssertion(() => page.FindAll("[data-testid=vendor-header-business-item]").Count
            .Should().Be(_api.Country == "KR" ? 1 : 0, "the policies are read"));
        return page;
    }

    private static void Search(IRenderedComponent<Vendor> page, string name)
    {
        page.Find("[data-testid=vendor-search-v-name]").Change(name);
        page.Find("[data-testid=vendor-toolbar-search]").Click();
        page.WaitForAssertion(() => page.Field("vendor-header-v-name").IsReadOnly.Should().BeTrue());
    }

    private static void Configure(HttpClient http) => http.BaseAddress = new Uri("http://api.test");

    /// <summary>Vendor, code, localization, tax code and field policy API with two stored vendors (ACME, BETA).</summary>
    private sealed class FakeVendorApi : HttpMessageHandler
    {
        private static readonly VendorDto Acme = new(701, "ACME", "Kim", null, "555-0100", null, null, null, null, "NET_30", "Y", 70.5m,
            null, "1 Main St", null, "Springfield", "CA", "USA", "90001", "1 Main St", null, "Springfield", "CA", "USA", "90001",
            "ACME", null, 7, "NONE", null, null, null, null, null, AuditTimes.Created, "setup", AuditTimes.Updated, "kim");

        public List<HttpRequestMessage> Requests { get; } = [];

        public List<CreateVendorRequest> Created { get; } = [];

        public string Country { get; set; } = "US";

        public IReadOnlyList<ApiWarning> Warnings { get; set; } = [];

        public (HttpStatusCode Status, string Body)? UpdateProblem { get; set; }

        public string? DeleteProblem { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var path = request.RequestUri!.AbsolutePath;
            if (path.StartsWith("/api/codes/", StringComparison.Ordinal))
            {
                return Json(Codes(path["/api/codes/".Length..]));
            }
            if (path.StartsWith("/" + FieldPolicyRoutes.Base, StringComparison.Ordinal))
            {
                return Json(Policies());
            }
            if (path == "/" + LocalizationRoutes.Base)
            {
                return Json(new LocalizationDto(Country, "USD", "en", "America/New_York", Country == "KR" ? "VAT" : "SALES_TAX",
                    "MM/dd/yyyy", "DOCUMENT", [], [new CurrencyDto("USD", "US Dollar", 2, "US")], [], []));
            }
            if (path == "/" + TaxCodeRoutes.Base)
            {
                return Json(new List<TaxCodeDto>
                {
                    new(7, "NONE", "비과세", new Dictionary<string, string>(), "NONE", 0, null, null, false, true, false, AuditTimes.Updated),
                });
            }
            if (request.Method == HttpMethod.Get && path == "/" + VendorRoutes.Base)
            {
                return Json(new VendorListDto([Acme], 1));
            }
            if (request.Method == HttpMethod.Get && path == "/api/mm/vendors/701/next")
            {
                return Json(Acme with { Id = 702, VName = "BETA" });
            }
            if (request.Method == HttpMethod.Get && path == "/api/mm/vendors/701/previous")
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            if (request.Method == HttpMethod.Get && path == "/api/mm/vendors/701")
            {
                return Json(Acme);
            }
            if (request.Method == HttpMethod.Post)
            {
                var create = (await request.Content!.ReadFromJsonAsync<CreateVendorRequest>(cancellationToken))!;
                Created.Add(create);
                return Json(Acme with { Id = 703, VName = create.VName!.ToUpperInvariant(), BalAmt = 0 });
            }
            if (request.Method == HttpMethod.Put)
            {
                return UpdateProblem is { } problem ? Problem(problem.Status, problem.Body) : Json(Acme with { Warnings = Warnings });
            }
            if (request.Method == HttpMethod.Delete && path == "/api/mm/vendors/701")
            {
                return DeleteProblem is null
                    ? new HttpResponseMessage(HttpStatusCode.NoContent)
                    : Problem(HttpStatusCode.UnprocessableEntity, DeleteProblem);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        // The country pack values of countrypacks/{US,KR}/field-policy.yaml
        private List<FieldPolicyDto> Policies()
        {
            var kr = Country == "KR";
            return
            [
                Policy("VName", FieldPolicyStates.Required),
                Policy("State", kr ? FieldPolicyStates.Hidden : FieldPolicyStates.Required),
                Policy("ZipCode", FieldPolicyStates.Required),
                Policy("BusinessRegNo", kr ? FieldPolicyStates.Optional : FieldPolicyStates.Hidden),
                Policy("CeoName", kr ? FieldPolicyStates.Optional : FieldPolicyStates.Hidden),
                Policy("BusinessType", kr ? FieldPolicyStates.Optional : FieldPolicyStates.Hidden),
                Policy("BusinessItem", kr ? FieldPolicyStates.Optional : FieldPolicyStates.Hidden),
            ];
        }

        private static FieldPolicyDto Policy(string field, string state) =>
            new($"Mm.Vendor.{field}", state, null, $"Mm.Vendor.{field}", "CountryPack");

        private static IReadOnlyList<CodeItemDto> Codes(string major) => major switch
        {
            "TERMS" => [new("NET_30", "NET 30"), new("NET_60", "NET 60")],
            "VALID_FLAG" => [new("Y", "사용"), new("N", "사용 안 함")],
            "STATE" => [new("CA", "California"), new("NY", "New York")],
            _ => [],
        };

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

        private static HttpResponseMessage Problem(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, null, "application/problem+json") };
    }
}
