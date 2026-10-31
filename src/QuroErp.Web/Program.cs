using QuroErp.Web.Auth;
using QuroErp.Web.Bc.Items;
using QuroErp.Web.Common;
using QuroErp.Web.Components;
using QuroErp.Web.Components.Layout;
using QuroErp.Web.Components.Pages.Samples;
using QuroErp.Web.Components.Screens;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddWebAuthentication(builder.Configuration);
builder.Services.AddWebApiClients(builder.Configuration);

// i18n.md: 리소스는 Resources/, 언어는 ko(기본)·en
builder.Services.AddWebLocalization();

// Screen type components (screens.md)
builder.Services.AddScoped<IQuroConfirm, JsQuroConfirm>();

// App shell: top bar and side menu from /me (app-shell.md)
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ShellProfileLoader>();
if (builder.Environment.IsDevelopment())
{
    // In-memory data of the screen type sample pages (/samples/*)
    builder.Services.AddSingleton<SampleStore>();
    builder.Services.AddSingleton<SamplePoStore>();
}

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseWebLocalization();

app.UseAuthentication();
app.UseAutoLogin();
app.UseSessionActivity();
app.UsePasswordChangeRedirect();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapLogout();
app.MapSession();
app.MapCulture();
app.MapItemImage();

app.Run();

/// <summary>통합 테스트(WebApplicationFactory)에서 쓴다.</summary>
public partial class Program;
