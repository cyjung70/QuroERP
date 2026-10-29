using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuroErp.Contracts.Bc.Items;
using QuroErp.Contracts.Bc.Localization;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Contracts.Common.Codes;
using QuroErp.Web.Auth;
using QuroErp.Web.Bc.Items;
using QuroErp.Web.Bc.Localization;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Pages.Bc;
using QuroErp.Web.Components.Screens;
using QuroErp.Web.Tests.Screens;

namespace QuroErp.Web.Tests.Bc;

/// <summary>Item master screen /bc/item (BC-UC-009, TASK-BC-018) against a fake item, code and localization API.</summary>
public class ItemScreenTests : BunitContext
{
    private readonly FakeItemApi _api = new();
    private readonly FakeConfirm _confirm = new();
    private readonly BunitAuthorizationContext _auth;

    public ItemScreenTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("ko");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddWebLocalization();
        Services.AddSingleton<IQuroConfirm>(_confirm);
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton<AccessTokenStore>();
        Services.AddScoped<AccessTokenProvider>();
        Services.AddHttpClient<AuthApiClient>(http => http.BaseAddress = new Uri("http://api.test"));
        Services.AddHttpClient<ItemApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<CodeApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<LocalizationApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        _auth = AddAuthorization();
    }

    [Fact, Trait("Id", "BC-UC-009-T01"), Trait("Id", "BC-UC-009-T09"), Trait("Id", "BC-UC-009-T10")]
    public void New_ShowsDefaultsCodeNamesAndDecimals()
    {
        var page = RenderAs("admin");

        Selected(page, "item-header-item-type").Should().Be("INVENTORY");
        Selected(page, "item-header-unit").Should().Be("EA");
        page.Find("[data-testid=item-header-item-type] option[value=INVENTORY]").TextContent.Should().Be("재고 품목");
        page.Find("[data-testid=item-header-valid-flag-y]").HasAttribute("checked").Should().BeTrue();
        Value(page, "item-header-usd-prc").Should().Be("0.01");
        page.Find("[data-testid=item-header-usd-prc]").GetAttribute("step").Should().Be("0.01");
        Value(page, "item-header-reorder-point").Should().Be("0.000");
        Value(page, "item-header-stk-val").Should().Be("0.00");
        page.Find("[data-testid=item-header-mvg-cost]").HasAttribute("readonly").Should().BeTrue();
        _api.Requests.Should().OnlyContain(r => r.Headers.Authorization!.Parameter == "token-admin");
    }

    [Fact, Trait("Id", "CORE-020-WT-01"), Trait("Id", "BC-UC-009-T12")]
    public void PreviousNext_AreAfterSearch_AndDisabledForANewRecord()
    {
        var page = RenderAs("admin");

        page.FindAll("[role=toolbar] button").Select(b => b.GetAttribute("data-testid")).Should().StartWith(
            ["item-toolbar-search", "item-toolbar-previous", "item-toolbar-next", "item-toolbar-new"]);
        page.Find("[data-testid=item-toolbar-previous]").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=item-toolbar-next]").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact, Trait("Id", "CORE-020-WT-02"), Trait("Id", "BC-UC-009-T12")]
    public void Next_LoadsTheFollowingItem_AndTheSearchCode()
    {
        var page = RenderAs("admin");
        Search(page, "A100");

        page.Find("[data-testid=item-toolbar-next]").Click();

        page.WaitForAssertion(() => Value(page, "item-header-item-cd").Should().Be("B200"));
        Value(page, "item-search-item-cd").Should().Be("B200");
        page.Find("[data-testid=item-toolbar-previous]").Click();
        page.WaitForAssertion(() => Value(page, "item-header-item-cd").Should().Be("A100"));
    }

    [Fact, Trait("Id", "CORE-020-WT-03"), Trait("Id", "BC-UC-009-T12")]
    public void PreviousAtTheFirstItem_StaysAndSaysSo()
    {
        var page = RenderAs("admin");
        Search(page, "A100");

        page.Find("[data-testid=item-toolbar-previous]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=item-message]").TextContent.Should().Be("처음 자료입니다."));
        Value(page, "item-header-item-cd").Should().Be("A100");
    }

    [Fact, Trait("Id", "CORE-020-WT-04"), Trait("Id", "BC-UC-009-T12")]
    public void Next_WithUnsavedChanges_AsksFirst_AndStaysWhenDeclined()
    {
        var page = RenderAs("admin");
        Search(page, "A100");
        page.Find("[data-testid=item-header-item-name]").Change("PEAR");
        _confirm.Answer = false;

        page.Find("[data-testid=item-toolbar-next]").Click();

        _confirm.Questions.Should().ContainSingle();
        Value(page, "item-header-item-cd").Should().Be("A100");
        Value(page, "item-header-item-name").Should().Be("PEAR");
        _api.Requests.Should().NotContain(r => r.RequestUri!.AbsolutePath.EndsWith("/next", StringComparison.Ordinal));
    }

    [Fact, Trait("Id", "BC-UC-009-T02")]
    public void SaveNew_PostsItem_AndLocksCode()
    {
        var page = RenderAs("admin");
        page.Find("[data-testid=item-header-item-cd]").Change("b200");
        page.Find("[data-testid=item-header-item-name]").Change("bolt");
        page.Find("[data-testid=item-header-item-class]").Change("HW");

        page.Find("[data-testid=item-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=item-message]").TextContent.Should().Be("저장했습니다."));
        var created = _api.Created.Should().ContainSingle().Subject;
        created.ItemCd.Should().Be("B200");
        created.ItemName.Should().Be("BOLT");
        created.ItemType.Should().Be("INVENTORY");
        created.UsdPrc.Should().Be(0.01m);
        page.Find("[data-testid=item-header-item-cd]").HasAttribute("readonly").Should().BeTrue();
        Value(page, "item-search-item-cd").Should().Be("B200");
    }

    [Fact, Trait("Id", "BC-UC-009-T03")]
    public void SaveWithoutItemClass_ShowsRequiredMessage_AndDoesNotPost()
    {
        var page = RenderAs("admin");
        page.Find("[data-testid=item-header-item-cd]").Change("B200");
        page.Find("[data-testid=item-header-item-name]").Change("BOLT");

        page.Find("[data-testid=item-toolbar-save]").Click();

        page.Find("[data-testid=item-message]").TextContent.Should().Be("품목군을(를) 입력하세요.");
        _api.Created.Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UC-009-T13")]
    public void UpdateAfterSomeoneElseSaved_Shows409Message()
    {
        var page = RenderAs("admin");
        Search(page, "A100");
        _api.UpdateConflict = true;
        page.Find("[data-testid=item-header-item-name]").Change("APPLE 2");

        page.Find("[data-testid=item-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=item-message]").TextContent
            .Should().Be("다른 사용자가 먼저 수정했습니다. 다시 조회한 뒤 저장하세요."));
    }

    [Theory, Trait("Id", "BC-UC-009-T04"), Trait("Id", "BC-UC-009-T05"), Trait("Id", "BC-UC-009-T06")]
    [InlineData("MM-E-801", "A100", "품목 코드 'A100'을(를) 쓸 수 없습니다. 대문자 18자 이하로, 다른 품목과 겹치지 않게 입력하세요.")]
    [InlineData("MM-E-803", "UsdPrc", "구매 단가은(는) 0 이상이어야 합니다.")]
    [InlineData("MM-E-806", "A100", "품목 A100은(는) 재고·재고 이동·세트 구성이 있어 유형을 바꿀 수 없습니다.")]
    public void SaveRejectedByTheApi_ShowsTheMessage_WithFieldLabels(string code, string arg, string message)
    {
        var page = RenderAs("admin");
        Search(page, "A100");
        _api.UpdateProblem = $$"""{"status":422,"code":"{{code}}","args":["{{arg}}"]}""";

        page.Find("[data-testid=item-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=item-message]").TextContent.Should().Be(message));
    }

    [Fact, Trait("Id", "BC-UC-009-T07")]
    public void DeletingAUsedItem_ShowsMmE807_AndKeepsTheItem()
    {
        var page = RenderAs("admin");
        Search(page, "A100");
        _api.DeleteProblem = """{"status":422,"code":"MM-E-807","args":["A100"]}""";

        page.Find("[data-testid=item-toolbar-delete]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=item-message]").TextContent
            .Should().Be("품목 A100은(는) 사용 중이라 지울 수 없습니다. 사용 안 함으로 바꾸세요."));
        Value(page, "item-header-item-cd").Should().Be("A100");
    }

    [Fact, Trait("Id", "MM-UT-802")]
    public void SaveWithUnknownCode_Shows422Message()
    {
        var page = RenderAs("admin");
        Search(page, "A100");
        _api.UpdateProblem = """{"status":422,"code":"Common-E-002","args":["UNIT","XX"]}""";

        page.Find("[data-testid=item-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=item-message]").TextContent.Should().Contain("'XX'"));
    }

    [Fact, Trait("Id", "BC-UC-009-T02")]
    public void Search_ByTypedCode_LoadsExactItem_AndShowsName()
    {
        var page = RenderAs("admin");

        Search(page, "a100");

        Value(page, "item-header-item-name").Should().Be("APPLE");
        Value(page, "item-search-item-name").Should().Be("APPLE");
        Value(page, "item-header-onhand-qty").Should().Be("5.000");
        page.Find("[data-testid=item-header-item-cd]").HasAttribute("readonly").Should().BeTrue();
    }

    [Fact, Trait("Id", "BC-UC-009-T11")]
    public void Lookup_SelectedItem_IsLoadedById()
    {
        var page = RenderAs("admin");

        page.Find("[data-testid=item-search-item-lookup]").Click();
        page.WaitForElement("[data-testid=item-item-lookup] tbody tr");
        page.Find("[data-testid=item-item-lookup] tbody tr button").Click();

        page.WaitForAssertion(() => Value(page, "item-header-item-cd").Should().Be("A100"));
        _api.Requests.Should().Contain(r => r.RequestUri!.AbsolutePath == "/api/bc/items/1");
    }

    [Fact, Trait("Id", "BC-UT-214")]
    public void UserWithoutFullAccess_SeesSearchAndMoveOnly_AndLockedInputs()
    {
        var page = RenderAs("user");
        Search(page, "A100");

        page.FindAll("[role=toolbar] button").Select(b => b.GetAttribute("data-testid"))
            .Should().Equal("item-toolbar-search", "item-toolbar-previous", "item-toolbar-next");
        page.Find("[data-testid=item-header-item-name]").HasAttribute("readonly").Should().BeTrue();
        page.Find("[data-testid=item-header-unit]").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=item-header-valid-flag-n]").HasAttribute("disabled").Should().BeTrue();
    }

    [Theory, Trait("Id", "BC-UC-009-T08")]
    [InlineData("SALES_TAX", "item-header-taxable", "item-header-vat-kind")]
    [InlineData("VAT", "item-header-vat-kind", "item-header-taxable")]
    public void TaxField_FollowsTaxMethod(string taxMethod, string shown, string hidden)
    {
        _api.TaxMethod = taxMethod;

        var page = RenderAs("admin");

        page.FindAll($"[data-testid={shown}]").Should().ContainSingle();
        page.FindAll($"[data-testid={hidden}]").Should().BeEmpty();
    }

    private IRenderedComponent<Item> RenderAs(string role)
    {
        var store = Services.GetRequiredService<AccessTokenStore>();
        var sessionId = store.Add(new LoginResponse($"token-{role}", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", role, false, "ko"));
        _auth.SetAuthorized("kim");
        _auth.SetRoles(role);
        _auth.SetClaims(new Claim(WebAuthClaims.SessionId, sessionId));
        var page = Render<Item>();
        page.WaitForElement("[data-testid=item-header-item-cd]");
        page.WaitForAssertion(() => page.FindAll("[data-testid=item-header-unit] option").Should().HaveCountGreaterThan(1));
        return page;
    }

    private static void Search(IRenderedComponent<Item> page, string code)
    {
        page.Find("[data-testid=item-search-item-cd]").Change(code);
        page.Find("[data-testid=item-toolbar-search]").Click();
        page.WaitForAssertion(() => page.Find("[data-testid=item-header-item-cd]").HasAttribute("readonly").Should().BeTrue());
    }

    private static string? Value(IRenderedComponent<Item> page, string testId) =>
        page.Find($"[data-testid={testId}]").GetAttribute("value");

    private static string? Selected(IRenderedComponent<Item> page, string testId) =>
        page.Find($"[data-testid={testId}] option[selected]").GetAttribute("value");

    private static void Configure(HttpClient http) => http.BaseAddress = new Uri("http://api.test");

    /// <summary>Item, code and localization API with one stored item (A100).</summary>
    private sealed class FakeItemApi : HttpMessageHandler
    {
        private ItemDto _item = new(1, "A100", "APPLE", null, null, "INVENTORY", "EA", "FR", "Y", 1.5m, 2m, null, null, 0m,
            null, null, null, null, null, null, true, "TAXABLE", "A100", 1.2m, 5m, 6m, DateTimeOffset.UnixEpoch);

        public List<HttpRequestMessage> Requests { get; } = [];

        public List<CreateItemRequest> Created { get; } = [];

        public string TaxMethod { get; set; } = "SALES_TAX";

        public bool UpdateConflict { get; set; }

        public string? UpdateProblem { get; set; }

        public string? DeleteProblem { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var path = request.RequestUri!.AbsolutePath;
            if (path.StartsWith("/api/codes/", StringComparison.Ordinal))
            {
                return Json(Codes(path["/api/codes/".Length..]));
            }
            if (path == "/" + LocalizationRoutes.Base)
            {
                return Json(new LocalizationDto("US", "USD", "en", TaxMethod, "MM/dd/yyyy", "DOCUMENT", [],
                    [new CurrencyDto("USD", "US Dollar", 2, "US")],
                    [new CountryDecimalDto("US", "MM", 2), new CountryDecimalDto("US", "SD", 4)],
                    [new DecimalSettingDto("MM", 3, "HALF_UP")]));
            }
            if (request.Method == HttpMethod.Get && path == "/" + ItemRoutes.Base)
            {
                return Json(new ItemListDto([_item], 1));
            }
            // Item class, item code order: A100 (FR) is first, B200 (HW) is last
            if (request.Method == HttpMethod.Get && path is "/api/bc/items/1/previous" or "/api/bc/items/2/next")
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            if (request.Method == HttpMethod.Get && path == "/api/bc/items/1/next")
            {
                return Json(_item with { Id = 2, ItemCd = "B200", ItemName = "BOLT", ItemClass = "HW" });
            }
            if (request.Method == HttpMethod.Get && path == "/api/bc/items/2/previous")
            {
                return Json(_item);
            }
            if (request.Method == HttpMethod.Get && path == "/api/bc/items/1")
            {
                return Json(_item);
            }
            if (request.Method == HttpMethod.Post)
            {
                var create = (await request.Content!.ReadFromJsonAsync<CreateItemRequest>(cancellationToken))!;
                Created.Add(create);
                return Json(_item with { Id = 2, ItemCd = create.ItemCd!, ItemName = create.ItemName, ItemClass = create.ItemClass });
            }
            if (request.Method == HttpMethod.Put)
            {
                if (UpdateConflict)
                {
                    return Problem(HttpStatusCode.Conflict, """{"status":409,"code":"Common-E-004"}""");
                }
                if (UpdateProblem is not null)
                {
                    return Problem(HttpStatusCode.UnprocessableEntity, UpdateProblem);
                }
                var update = (await request.Content!.ReadFromJsonAsync<UpdateItemRequest>(cancellationToken))!;
                _item = _item with { ItemName = update.ItemName };
                return Json(_item);
            }
            if (request.Method == HttpMethod.Delete && path == "/api/bc/items/1")
            {
                return DeleteProblem is null ? new HttpResponseMessage(HttpStatusCode.NoContent) : Problem(HttpStatusCode.UnprocessableEntity, DeleteProblem);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static IReadOnlyList<CodeItemDto> Codes(string major) => major switch
        {
            "ITEM_TYPE" => [new("INVENTORY", "재고 품목"), new("SET", "세트"), new("SERVICE", "서비스")],
            "UNIT" => [new("EA", "개"), new("BX", "박스")],
            "ITEM_CLASS" => [new("FR", "과일"), new("HW", "철물")],
            "VALID_FLAG" => [new("Y", "사용"), new("N", "사용 안 함")],
            "GRADE" => [new("A01", "A01")],
            _ => [],
        };

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

        private static HttpResponseMessage Problem(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, null, "application/problem+json") };
    }
}
