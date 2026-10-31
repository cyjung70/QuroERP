using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using QuroErp.Contracts.Bc.Items;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Web.Auth;
using QuroErp.Web.Bc.Items;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Pages.Bc;
using QuroErp.Web.Components.Screens;
using QuroErp.Web.Tests.Screens;

namespace QuroErp.Web.Tests.Bc;

/// <summary>Item image upload screen /bc/item-image-upload (BC-UC-010, TASK-BC-020) against a fake item API.</summary>
public class ItemImageUploadScreenTests : BunitContext
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];

    private readonly FakeItemImageApi _api = new();
    private readonly FakeConfirm _confirm = new();
    private readonly BunitAuthorizationContext _auth;

    public ItemImageUploadScreenTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("ko");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddWebLocalization();
        Services.AddSingleton<IQuroConfirm>(_confirm);
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton<AccessTokenStore>();
        Services.AddScoped<AccessTokenProvider>();
        Services.AddHttpClient<AuthApiClient>(http => http.BaseAddress = new Uri("http://api.test"));
        Services.AddHttpClient<ItemApiClient>(http => http.BaseAddress = new Uri("http://api.test"))
            .ConfigurePrimaryHttpMessageHandler(() => _api);
        _auth = AddAuthorization();
    }

    [Fact, Trait("Id", "MM-UT-820")]
    public void Open_WithAnItemCode_LoadsTheItem_AndShowsItsImageFromTheWebHost()
    {
        var page = RenderAs("admin", "a100");

        page.WaitForAssertion(() => Value(page, "item-image-upload-header-item-cd").Should().Be("A100"));
        Value(page, "item-image-upload-header-item-name").Should().Be("APPLE");
        Value(page, "item-image-upload-search-item-name").Should().Be("APPLE");
        ImageSrc(page).Should().Be("/bc/item-image/A100?v=1");
    }

    [Fact, Trait("Id", "MM-UT-820")]
    public void ImageNotFound_ShowsNoImage()
    {
        var page = RenderAs("admin", "A100");
        page.WaitForAssertion(() => ImageSrc(page).Should().NotBeNull());

        page.Find("[data-testid=item-image-upload-header-image] img").TriggerEvent("onerror", new Microsoft.AspNetCore.Components.Web.ErrorEventArgs());

        page.Find("[data-testid=item-image-upload-header-image]").TextContent.Should().Be("이미지가 없습니다.");
    }

    [Fact, Trait("Id", "MM-UT-822"), Trait("Id", "MM-UT-824"), Trait("Id", "BC-UC-010-T01")]
    public void Save_UploadsThePickedFile_AndShowsTheImageAgain()
    {
        var page = RenderAs("admin", "A100");
        Pick(page, Jpeg, "new.jpg");

        page.Find("[data-testid=item-image-upload-toolbar-save]").Click();

        page.WaitForAssertion(() => Message(page).Should().Be("저장했습니다."));
        var upload = _api.Uploads.Should().ContainSingle().Subject;
        upload.Path.Should().Be("/api/bc/items/A100/image");
        upload.Field.Should().Be(ItemRoutes.ImageFormField);
        upload.FileName.Should().Be("new.jpg");
        upload.Content.Should().Equal(Jpeg);
        ImageSrc(page).Should().Be("/bc/item-image/A100?v=2");
    }

    [Fact, Trait("Id", "MM-UT-821"), Trait("Id", "BC-UC-010-T02")]
    public void Save_FileTheApiRejects_ShowsMmE821_AndKeepsTheImage()
    {
        _api.UploadProblem = """{"status":400,"code":"MM-E-821"}""";
        var page = RenderAs("admin", "A100");
        Pick(page, [0x89, 0x50, 0x4E, 0x47], "p.jpg");

        page.Find("[data-testid=item-image-upload-toolbar-save]").Click();

        page.WaitForAssertion(() => Message(page).Should().Be("10MB 이하의 JPEG 파일 1개를 고르세요. 품목이 있어야 합니다."));
        ImageSrc(page).Should().Be("/bc/item-image/A100?v=1");
    }

    [Fact, Trait("Id", "MM-UT-821")]
    public void Save_FileOver10MegabytesOrNoFile_IsNotSent()
    {
        var page = RenderAs("admin", "A100");

        page.Find("[data-testid=item-image-upload-toolbar-save]").Click();
        page.WaitForAssertion(() => Message(page).Should().StartWith("10MB 이하의 JPEG"));
        Pick(page, new byte[ItemRoutes.ImageMaxBytes + 1], "big.jpg");
        page.Find("[data-testid=item-image-upload-toolbar-save]").Click();

        page.WaitForAssertion(() => Message(page).Should().StartWith("10MB 이하의 JPEG"));
        _api.Uploads.Should().BeEmpty();
    }

    [Fact, Trait("Id", "MM-UT-823"), Trait("Id", "BC-UC-010-T03")]
    public void Delete_AsksFirst_ThenClearsTheImage_AndShowsItAgain()
    {
        var page = RenderAs("admin", "A100");
        _confirm.Answer = false;

        page.Find("[data-testid=item-image-upload-toolbar-delete]").Click();

        _confirm.Questions.Should().ContainSingle().Which.Should().Be("품목 이미지를 지울까요? 품목 그룹 이미지가 있으면 그 이미지를 보입니다.");
        _api.Deletes.Should().BeEmpty();

        _confirm.Answer = true;
        page.Find("[data-testid=item-image-upload-toolbar-delete]").Click();

        page.WaitForAssertion(() => Message(page).Should().Be("삭제했습니다."));
        _api.Deletes.Should().Equal("/api/bc/items/A100/image");
        ImageSrc(page).Should().Be("/bc/item-image/A100?v=2");
    }

    [Fact, Trait("Id", "MM-UT-824")]
    public void UserWithoutFullAccess_SeesTheImage_ButCannotUploadOrDelete()
    {
        var page = RenderAs("user", "A100");

        ImageSrc(page).Should().Be("/bc/item-image/A100?v=1");
        page.FindAll("[role=toolbar] button").Select(b => b.GetAttribute("data-testid")).Should().Equal("item-image-upload-toolbar-search");
        page.FindAll("[data-testid=item-image-upload-header-file]").Should().BeEmpty();
        page.FindAll("[data-testid=item-image-upload-toolbar-delete]").Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UC-010-T04")]
    public void Search_UnknownItem_SaysNoData_AndShowsNoImage()
    {
        var page = RenderAs("admin");

        page.Find("[data-testid=item-image-upload-search-item-cd]").Change("Z999");
        page.Find("[data-testid=item-image-upload-toolbar-search]").Click();

        page.WaitForAssertion(() => Message(page).Should().Be("조회된 내용이 없습니다."));
        page.FindAll("[data-testid=item-image-upload-header-image] img").Should().BeEmpty();
    }

    private IRenderedComponent<ItemImageUpload> RenderAs(string role, string? itemCd = null)
    {
        var store = Services.GetRequiredService<AccessTokenStore>();
        var sessionId = store.Add(new LoginResponse($"token-{role}", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", role, false, "ko"));
        _auth.SetAuthorized("kim");
        _auth.SetRoles(role);
        _auth.SetClaims(new Claim(WebAuthClaims.SessionId, sessionId));
        if (itemCd is not null)
        {
            Services.GetRequiredService<NavigationManager>().NavigateTo(ItemImageUpload.PathOf(itemCd));
        }
        var page = Render<ItemImageUpload>();
        page.WaitForElement("[data-testid=item-image-upload-header-item-cd]");
        if (itemCd is not null)
        {
            page.WaitForAssertion(() => ImageSrc(page).Should().NotBeNull());
        }
        return page;
    }

    private static void Pick(IRenderedComponent<ItemImageUpload> page, byte[] content, string fileName) =>
        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(content, fileName, contentType: "image/jpeg"));

    private static string? Value(IRenderedComponent<ItemImageUpload> page, string testId) =>
        page.Find($"[data-testid={testId}]").GetAttribute("value");

    private static string? ImageSrc(IRenderedComponent<ItemImageUpload> page) =>
        page.FindAll("[data-testid=item-image-upload-header-image] img").SingleOrDefault()?.GetAttribute("src");

    private static string Message(IRenderedComponent<ItemImageUpload> page) =>
        page.Find("[data-testid=item-image-upload-message]").TextContent;

    /// <summary>Item API with one item (A100) and its image routes.</summary>
    private sealed class FakeItemImageApi : HttpMessageHandler
    {
        private readonly ItemDto _item = new(1, "A100", "APPLE", null, null, "INVENTORY", "EA", "FR", "Y", 1.5m, 2m, null, null, 0m,
            null, null, null, null, null, null, true, "TAXABLE", "A100", 1.2m, 5m, 6m, DateTimeOffset.UnixEpoch);

        public List<(string Path, string? Field, string? FileName, byte[] Content)> Uploads { get; } = [];

        public List<string> Deletes { get; } = [];

        public string? UploadProblem { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path == "/" + ItemRoutes.Base)
            {
                var items = request.RequestUri.Query.Contains("itemCd=A100", StringComparison.Ordinal) ? [_item] : Array.Empty<ItemDto>();
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new ItemListDto(items, items.Length)) };
            }
            if (request.Method == HttpMethod.Put && path == "/api/bc/items/A100/image")
            {
                var file = ((MultipartFormDataContent)request.Content!).Single();
                var disposition = file.Headers.ContentDisposition!;
                Uploads.Add((path, disposition.Name?.Trim('"'), disposition.FileName?.Trim('"'), await file.ReadAsByteArrayAsync(cancellationToken)));
                return UploadProblem is null
                    ? new HttpResponseMessage(HttpStatusCode.NoContent)
                    : new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(UploadProblem, null, "application/problem+json") };
            }
            if (request.Method == HttpMethod.Delete && path == "/api/bc/items/A100/image")
            {
                Deletes.Add(path);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
