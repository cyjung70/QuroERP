using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using QuroErp.Web.Common;

namespace QuroErp.Web.Bc.Items;

/// <summary>
/// Item image for an img src (BC-UC-010). The browser loads it here with the session cookie instead of through the
/// screen's SignalR connection, as images are up to 10 MB (MM-BR-821). The Web host asks the API with the user's token.
/// </summary>
public static class ItemImageEndpoint
{
    public const string Path = "/bc/item-image";

    /// <param name="version">Changes after an upload or delete so the browser does not show its cached image.</param>
    public static string Of(string itemCd, int version) =>
        $"{Path}/{Uri.EscapeDataString(itemCd)}?v={version.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>200 with the item image or its item group's (MM-BR-820), else the API status (404 when there is none).</summary>
    public static IEndpointRouteBuilder MapItemImage(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Path + "/{itemCd}", async (string itemCd, HttpContext context, AuthenticationStateProvider authentication, ItemApiClient api) =>
            {
                // Outside a component nothing has set the user yet; the API client's token provider reads it from here
                ((IHostEnvironmentAuthenticationStateProvider)authentication).SetAuthenticationState(
                    Task.FromResult(new AuthenticationState(context.User)));
                // An img gets no error page body
                if (context.Features.Get<IStatusCodePagesFeature>() is { } statusCodePages)
                {
                    statusCodePages.Enabled = false;
                }
                context.Response.Headers.CacheControl = "private, no-cache";
                try
                {
                    var image = await api.GetImageAsync(itemCd, context.RequestAborted);
                    return image is null ? Results.NotFound() : Results.File(image.Content, image.ContentType);
                }
                catch (ApiProblemException e)
                {
                    return Results.StatusCode(e.Problem.Status);
                }
                catch (HttpRequestException)
                {
                    return Results.StatusCode((int)HttpStatusCode.BadGateway);
                }
            })
            .RequireAuthorization();
        return endpoints;
    }
}
