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
using QuroErp.Contracts.Sd.Customers;
using QuroErp.Web.Auth;
using QuroErp.Web.Bc.Localization;
using QuroErp.Web.Bc.Tax;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Pages.Sd;
using QuroErp.Web.Components.Pages.Sd.Models;
using QuroErp.Web.Components.Screens;
using QuroErp.Web.Sd.Customers;
using QuroErp.Web.Tests.Screens;

namespace QuroErp.Web.Tests.Sd;

/// <summary>Customer master screen /sd/customer (SD-UC-001, TASK-SD-002) against a fake customer, code, tax and policy API.</summary>
public class CustomerScreenTests : BunitContext
{
    private const string ExemptionGrid = "customer-tax-exemptions-grid";

    private readonly FakeCustomerApi _api = new();
    private readonly BunitAuthorizationContext _auth;

    public CustomerScreenTests()
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
        Services.AddHttpClient<CustomerApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<CodeApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<LocalizationApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<TaxCodeApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<FieldPolicyApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        _auth = AddAuthorization();
    }

    [Fact, Trait("Id", "SD-UT-301"), Trait("Rule", "SD-BR-301")]
    public void New_ShowsTheDefaults_AndBalanceIsReadOnly()
    {
        var page = RenderAs("admin");

        // The combo box keys its options in lower case
        page.Field("customer-header-terms").Selected.Should().BeEquivalentTo("COD_CHECK");
        page.Find("[data-testid=customer-header-valid-flag-y]").HasAttribute("checked").Should().BeTrue();
        page.Field("customer-header-country").Value.Should().Be("USA");
        page.Field("customer-header-ship-country").Value.Should().Be("USA");
        page.Field("customer-header-bal-amt").Value.Should().Be("0.00");
        page.Field("customer-header-bal-amt").IsReadOnly.Should().BeTrue();
        page.Field("customer-header-rep").Options.Should().Contain(("kim", "Kim"));
        page.Field("customer-header-billing-flow").Options.Select(o => o.Text)
            .Should().Equal("출하와 함께 청구", "출하 후 따로 청구", "청구 후 출하");
        page.Field("customer-header-billing-flow").Selected.Should().BeNullOrEmpty("the company setting decides on save (SD-BR-335)");
        page.Find("[data-testid=customer-header-tax-code]").Closest(".quro-field")!.ClassList
            .Should().Contain("quro-field-required", "a US customer needs a tax code (SD-BR-331)");
        page.FindAll("[data-testid=customer-tax-exemptions]").Should().BeEmpty("a new customer has no certificates yet");
    }

    [Fact, Trait("Id", "BC-UT-434"), Trait("Rule", "BC-BR-434")]
    public void UsPolicies_HideTheKrFields_AndTheTaxPartyType()
    {
        var page = RenderAs("admin");

        page.FindAll("[data-testid=customer-header-business-reg-no]").Should().BeEmpty();
        page.FindAll("[data-testid=customer-header-ceo-name]").Should().BeEmpty();
        page.FindAll("[data-testid=customer-header-tax-party-type]").Should().BeEmpty();
        page.Find("[data-testid=customer-header-state]").Closest(".quro-field")!.ClassList.Should().Contain("quro-field-required");
    }

    [Fact, Trait("Id", "SD-UT-332"), Trait("Rule", "SD-BR-332")]
    public void KrCompany_ShowsTheKrFields_AndNoExemptionSection()
    {
        _api.Country = "KR";

        var page = RenderAs("admin");
        Search(page, "ACME");

        page.Field("customer-header-tax-party-type").Options.Should().Contain([("business", "사업자"), ("individual", "개인")]);
        page.FindAll("[data-testid=customer-header-business-reg-no]").Should().ContainSingle();
        page.FindAll("[data-testid=customer-header-state]").Should().BeEmpty();
        page.FindAll("[data-testid=customer-tax-exemptions]").Should().BeEmpty("the KR policy hides the certificates");
        _api.Requests.Should().NotContain(r => r.RequestUri!.AbsolutePath.EndsWith("/tax-exemptions", StringComparison.Ordinal));
    }

    [Fact, Trait("Id", "SD-UC-001-T01")]
    public void SaveNew_PostsTheCustomer_WithTheTaxCodeId_AndOpensTheExemptionSection()
    {
        var page = RenderAs("admin");
        page.Field("customer-header-c-name").Change("acme");
        page.Field("customer-header-ship-name").Change("acme");
        page.Field("customer-header-phone1").Change("555-0100");
        page.Field("customer-header-rep").Change("KIM");
        page.Field("customer-header-state").Change("CA");
        page.Field("customer-header-zip-code").Change("90001");
        page.Field("customer-header-tax-code").Change("NONE");

        page.Find("[data-testid=customer-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=customer-message]").TextContent.Should().Be("저장했습니다."));
        var created = _api.Created.Should().ContainSingle().Subject;
        created.CName.Should().Be("ACME");
        created.ShipName.Should().Be("ACME");
        created.Rep.Should().Be("KIM");
        created.Terms.Should().Be("COD_CHECK");
        created.TaxCodeId.Should().Be(7);
        created.BillingFlow.Should().BeNull("the API takes the company billing default");
        created.StartDt.Should().BeNull("the API takes the business date");
        page.Field("customer-header-c-name").IsReadOnly.Should().BeTrue();
        page.WaitForAssertion(() => page.FindAll("[data-testid=customer-tax-exemptions]").Should().ContainSingle());
    }

    [Fact, Trait("Id", "SD-UC-001-T02")]
    public void SaveWithoutRep_ShowsTheRequiredMessage_AndDoesNotPost()
    {
        var page = RenderAs("admin");
        page.Field("customer-header-c-name").Change("ACME");
        page.Field("customer-header-ship-name").Change("ACME");
        page.Field("customer-header-phone1").Change("555-0100");
        page.Field("customer-header-state").Change("CA");
        page.Field("customer-header-zip-code").Change("90001");
        page.Field("customer-header-tax-code").Change("NONE");

        page.Find("[data-testid=customer-toolbar-save]").Click();

        page.Find("[data-testid=customer-message]").TextContent.Should().Be("영업 담당을(를) 입력하세요.");
        _api.Created.Should().BeEmpty();
    }

    [Theory, Trait("Id", "SD-UC-001-T03"), Trait("Id", "SD-UT-331")]
    [InlineData("""{"status":422,"code":"SD-E-331","args":["TaxCodeId"]}""", "US 고객은 배송지 세금 코드(세금 코드)를 골라야 합니다.")]
    [InlineData("""{"status":422,"code":"SD-E-301","args":["ShipName"]}""", "필수 항목 배송지 이름을(를) 입력하세요.")]
    [InlineData("""{"status":409,"code":"Common-E-004"}""", "다른 사용자가 먼저 수정했습니다.")]
    public void SaveRejectedByTheApi_ShowsTheMessage_WithFieldLabels(string problem, string message)
    {
        var page = RenderAs("admin");
        Search(page, "ACME");
        _api.UpdateProblem = problem;

        page.Find("[data-testid=customer-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=customer-message]").TextContent.Should().Contain(message));
    }

    [Fact, Trait("Id", "SD-UT-522"), Trait("Rule", "BC-BR-522")]
    public void SaveWithAWarning_ShowsItAfterSaved()
    {
        var page = RenderAs("admin");
        Search(page, "ACME");
        _api.Warnings = [new ApiWarning("BC-W-522", ["BETA"])];

        page.Find("[data-testid=customer-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=customer-message]").TextContent
            .Should().Be("저장했습니다. 같은 사업자등록번호를 쓰는 거래처가 있습니다: BETA. 저장은 했습니다."));
    }

    [Fact, Trait("Id", "SD-UT-308"), Trait("Rule", "SD-BR-308")]
    public void Search_ByTypedName_LoadsTheCustomer_WithItsBalance_AndCertificates()
    {
        var page = RenderAs("admin");

        Search(page, "acme");

        page.Field("customer-header-phone1").Value.Should().Be("555-0100");
        page.Field("customer-header-bal-amt").Value.Should().Be("70.50");
        page.Field("customer-header-billing-flow").Selected.Should().BeEquivalentTo(CustomerModel.ShipThenBill);
        page.WaitForAssertion(() => Exemptions(page).Column("certificateNo").Should().Equal("EX-1"));
        Exemptions(page).Column("stateCode").Should().Equal("CA");
    }

    [Fact, Trait("Id", "SD-UT-524"), Trait("Rule", "BC-BR-524")]
    public void AddedAndDeletedCertificates_AreSavedTogether()
    {
        var page = RenderAs("admin");
        Search(page, "ACME");
        page.WaitForAssertion(() => Exemptions(page).Rows.Should().HaveCount(1));

        Exemptions(page).Select(0);
        page.Find("[data-testid=customer-tax-exemptions-delete-row]").Click();
        page.Find("[data-testid=customer-tax-exemptions-add-row]").Click();
        page.WaitForAssertion(() => Exemptions(page).Rows.Should().HaveCount(2));
        var added = Exemptions(page).Rows.Count - 1;
        Exemptions(page).Edit(added, "certificateNo", "ex-2").Should().BeTrue();
        Exemptions(page).Edit(added, "stateCode", "ny").Should().BeTrue();
        page.Find("[data-testid=customer-tax-exemptions-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=customer-tax-exemptions-message]").TextContent.Should().Be("저장했습니다."));
        var saved = _api.SavedExemptions.Should().ContainSingle().Subject;
        saved.Added.Should().ContainSingle().Which.Should().Be(new CustomerTaxExemptionSaveRow(null, "EX-2", "NY", null, null));
        saved.Updated.Should().BeEmpty();
        saved.Deleted.Should().Equal(new CustomerTaxExemptionKey(901, AuditTimes.Updated));
        Exemptions(page).Column("certificateNo").Should().Equal("EX-2");
    }

    [Fact, Trait("Id", "SD-UT-524"), Trait("Rule", "BC-BR-524")]
    public void RejectedCertificates_ShowTheMessage_WithTheColumnLabel()
    {
        var page = RenderAs("admin");
        Search(page, "ACME");
        page.WaitForAssertion(() => Exemptions(page).Rows.Should().HaveCount(1));
        _api.ExemptionProblem = """{"status":422,"code":"BC-E-524","args":["StateCode"]}""";
        page.Find("[data-testid=customer-tax-exemptions-add-row]").Click();
        page.WaitForAssertion(() => Exemptions(page).Rows.Should().HaveCount(2));

        page.Find("[data-testid=customer-tax-exemptions-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=customer-tax-exemptions-message]").TextContent
            .Should().StartWith("면세 증명서의 면세 주 값이 올바르지 않습니다."));
        Exemptions(page).Rows.Should().HaveCount(2, "the rows stay for the user to fix");
    }

    [Fact, Trait("Id", "SD-UT-524")]
    public void SavingCertificatesWithoutChanges_SaysSo()
    {
        var page = RenderAs("admin");
        Search(page, "ACME");

        page.Find("[data-testid=customer-tax-exemptions-save]").Click();

        page.Find("[data-testid=customer-tax-exemptions-message]").TextContent.Should().Be("저장할 변경 내용이 없습니다.");
        _api.SavedExemptions.Should().BeEmpty();
    }

    [Fact, Trait("Id", "SD-UT-307"), Trait("Rule", "SD-BR-307")]
    public void DeletingACustomerWithDocuments_ShowsSdE307_AndKeepsTheCustomer()
    {
        var page = RenderAs("admin");
        Search(page, "ACME");
        _api.DeleteProblem = """{"status":422,"code":"SD-E-307","args":["ACME"]}""";

        page.Find("[data-testid=customer-toolbar-delete]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=customer-message]").TextContent.Should().Contain("ACME"));
        page.Field("customer-header-c-name").Value.Should().Be("ACME");
    }

    [Fact, Trait("Id", "SD-UC-001-T01")]
    public void Lookup_SelectedCustomer_IsLoadedById()
    {
        var page = RenderAs("admin");

        page.Find("[data-testid=customer-search-c-name-lookup]").Click();
        page.WaitForAssertion(() => page.LookupRows("customer-customer-lookup").Should().NotBeEmpty());
        page.PickLookupRow<CustomerDto, Customer>("customer-customer-lookup", 0);

        page.WaitForAssertion(() => page.Field("customer-header-c-name").Value.Should().Be("ACME"));
        _api.Requests.Should().Contain(r => r.RequestUri!.AbsolutePath == "/api/sd/customers/801");
    }

    [Fact, Trait("Id", "BC-UT-214")]
    public void UserWithoutFullAccess_SeesLockedInputs_AndDisabledCommands()
    {
        var page = RenderAs("user");
        Search(page, "ACME");

        page.FindAll("[role=toolbar] button[disabled]").Select(b => b.GetAttribute("data-testid"))
            .Should().Contain(["customer-toolbar-new", "customer-toolbar-save", "customer-toolbar-delete"]);
        page.Field("customer-header-phone1").IsReadOnly.Should().BeTrue();
        page.Field("customer-header-rep").IsReadOnly.Should().BeTrue();
        page.Find("[data-testid=customer-tax-exemptions-add-row]").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=customer-tax-exemptions-save]").HasAttribute("disabled").Should().BeTrue();
        page.WaitForAssertion(() => Exemptions(page).IsReadOnly(0, "certificateNo").Should().BeTrue());
    }

    private static DataGridPage<CustomerTaxExemptionRow> Exemptions(IRenderedComponent<Customer> page) =>
        page.DataGrid<CustomerTaxExemptionRow, Customer>(ExemptionGrid);

    private IRenderedComponent<Customer> RenderAs(string role)
    {
        var store = Services.GetRequiredService<AccessTokenStore>();
        var sessionId = store.Add(new LoginResponse($"token-{role}", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", role, false, "ko"));
        _auth.SetAuthorized("kim");
        _auth.SetRoles(role);
        _auth.SetClaims(new Claim(WebAuthClaims.SessionId, sessionId));
        var page = Render<Customer>();
        page.WaitForElement("[data-testid=customer-header-c-name]");
        page.WaitForAssertion(() => page.Field("customer-header-terms").Options.Should().HaveCountGreaterThan(1));
        page.WaitForAssertion(() => page.FindAll("[data-testid=customer-header-business-item]").Count
            .Should().Be(_api.Country == "KR" ? 1 : 0, "the policies are read"));
        return page;
    }

    private static void Search(IRenderedComponent<Customer> page, string name)
    {
        page.Find("[data-testid=customer-search-c-name]").Change(name);
        page.Find("[data-testid=customer-toolbar-search]").Click();
        page.WaitForAssertion(() => page.Field("customer-header-c-name").IsReadOnly.Should().BeTrue());
    }

    private static void Configure(HttpClient http) => http.BaseAddress = new Uri("http://api.test");

    /// <summary>Customer, code, localization, tax code and field policy API with one stored customer (ACME) and its certificate.</summary>
    private sealed class FakeCustomerApi : HttpMessageHandler
    {
        private static readonly CustomerDto Acme = new(
            Id: 801, CName: "ACME", ShipName: "ACME", Contact: "Kim", Contact2: null, Phone1: "555-0100", Phone2: null, Phone3: null,
            FaxNo: null, Email: null, Rep: "KIM", PriceLevel: "ACME", FreightCharge: null, BalAmt: 70.5m, Remarks: null, Memo2: null,
            Memo3: null, Terms: "NET_30", ValidFlag: "Y", Addr1: "1 Main St", Addr2: null, City: "Springfield", State: "CA",
            Country: "USA", ZipCode: "90001", ShipAddr1: "1 Main St", ShipAddr2: null, ShipCity: "Springfield", ShipState: "CA",
            ShipCountry: "USA", ShipZipCode: "90001", ShipPhone: null, ShipFax: null, CreditLimit: 1000m, DcStatus: null,
            StartDt: new DateOnly(2026, 1, 5), BillingFlow: CustomerModel.ShipThenBill, CurrencyCode: null, TaxCodeId: 7,
            TaxCode: "NONE", TaxPartyType: null, BusinessRegNo: null, CeoName: null, BusinessType: null, BusinessItem: null,
            CreatedAt: AuditTimes.Created, CreatedBy: "setup", UpdatedAt: AuditTimes.Updated, UpdatedBy: "kim");

        private static readonly CustomerTaxExemptionDto Certificate = new(901, "EX-1", "CA", null, AuditTimes.Updated);

        public List<HttpRequestMessage> Requests { get; } = [];

        public List<CreateCustomerRequest> Created { get; } = [];

        public List<BatchRequest<CustomerTaxExemptionSaveRow, CustomerTaxExemptionKey>> SavedExemptions { get; } = [];

        public string Country { get; set; } = "US";

        public IReadOnlyList<ApiWarning> Warnings { get; set; } = [];

        public string? UpdateProblem { get; set; }

        public string? DeleteProblem { get; set; }

        public string? ExemptionProblem { get; set; }

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
            if (path.EndsWith("/" + CustomerRoutes.TaxExemptions, StringComparison.Ordinal))
            {
                return await ExemptionsAsync(request, cancellationToken);
            }
            if (request.Method == HttpMethod.Get && path == "/" + CustomerRoutes.Base)
            {
                return Json(new CustomerListDto([Acme], 1));
            }
            if (request.Method == HttpMethod.Get && path == "/api/sd/customers/801")
            {
                return Json(Acme);
            }
            if (request.Method == HttpMethod.Post)
            {
                var create = (await request.Content!.ReadFromJsonAsync<CreateCustomerRequest>(cancellationToken))!;
                Created.Add(create);
                return Json(Acme with { Id = 803, CName = create.CName!.ToUpperInvariant(), BalAmt = 0 });
            }
            if (request.Method == HttpMethod.Put)
            {
                return UpdateProblem is { } problem ? Problem(problem) : Json(Acme with { Warnings = Warnings });
            }
            if (request.Method == HttpMethod.Delete && path == "/api/sd/customers/801")
            {
                return DeleteProblem is null ? new HttpResponseMessage(HttpStatusCode.NoContent) : Problem(DeleteProblem);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        // A KR company has no certificates (404); a new customer has none yet
        private async Task<HttpResponseMessage> ExemptionsAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Country == "KR")
            {
                return Problem("""{"status":404,"code":"Common-E-006"}""");
            }
            if (request.Method == HttpMethod.Get)
            {
                return Json(request.RequestUri!.AbsolutePath.Contains("/801/", StringComparison.Ordinal)
                    ? new List<CustomerTaxExemptionDto> { Certificate }
                    : []);
            }
            if (ExemptionProblem is { } problem)
            {
                return Problem(problem);
            }
            var batch = (await request.Content!.ReadFromJsonAsync<BatchRequest<CustomerTaxExemptionSaveRow, CustomerTaxExemptionKey>>(cancellationToken))!;
            SavedExemptions.Add(batch);
            return Json(batch.Added.Select((r, i) => new CustomerTaxExemptionDto(950 + i, r.CertificateNo!, r.StateCode!, r.ValidTo, AuditTimes.Updated)).ToList());
        }

        // The country pack values of countrypacks/{US,KR}/field-policy.yaml
        private List<FieldPolicyDto> Policies()
        {
            var kr = Country == "KR";
            return
            [
                Policy("State", kr ? FieldPolicyStates.Hidden : FieldPolicyStates.Required),
                Policy("ShipState", kr ? FieldPolicyStates.Hidden : FieldPolicyStates.Optional),
                Policy("ZipCode", FieldPolicyStates.Required),
                Policy("BusinessRegNo", kr ? FieldPolicyStates.Optional : FieldPolicyStates.Hidden),
                Policy("CeoName", kr ? FieldPolicyStates.Optional : FieldPolicyStates.Hidden),
                Policy("BusinessType", kr ? FieldPolicyStates.Optional : FieldPolicyStates.Hidden),
                Policy("BusinessItem", kr ? FieldPolicyStates.Optional : FieldPolicyStates.Hidden),
                Policy("TaxExemptions", kr ? FieldPolicyStates.Hidden : FieldPolicyStates.Optional),
            ];
        }

        private static FieldPolicyDto Policy(string field, string state) =>
            new($"Sd.Customer.{field}", state, null, $"Sd.Customer.{field}", "CountryPack");

        private static IReadOnlyList<CodeItemDto> Codes(string major) => major switch
        {
            "TERMS" => [new("COD_CHECK", "COD 수표"), new("NET_30", "NET 30")],
            "VALID_FLAG" => [new("Y", "사용"), new("N", "사용 안 함")],
            "STATE" => [new("CA", "California"), new("NY", "New York")],
            "SALES_REP" => [new("KIM", "Kim")],
            "FREIGHT_CHARGE" => [new("Y", "Y"), new("N", "N")],
            "DISCOUNT_STATUS" => [new("A", "A")],
            _ => [],
        };

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

        private static HttpResponseMessage Problem(string body)
        {
            var status = (HttpStatusCode)int.Parse(body.Split("\"status\":")[1].Split(',', '}')[0], CultureInfo.InvariantCulture);
            return new(status) { Content = new StringContent(body, null, "application/problem+json") };
        }
    }
}
