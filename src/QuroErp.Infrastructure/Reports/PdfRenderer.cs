using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using QuroErp.Application.Common.Printing;

namespace QuroErp.Infrastructure.Reports;

/// <summary>
/// Razor template → HTML (Blazor HtmlRenderer) → PDF (Playwright, headless Chromium), reports.md "PDF". One Chromium per process,
/// started on first use; each conversion gets its own context and page with JavaScript off and every request aborted. At most
/// <see cref="PrintOptions.MaxConcurrency"/> conversions run at once, each limited to <see cref="PrintOptions.TimeoutSeconds"/>.
/// </summary>
public sealed class PdfRenderer(IServiceProvider services, ILoggerFactory loggerFactory, PrintOptions options) : IPdfRenderer, IAsyncDisposable
{
    // Filled by Chromium, not by page script, so it works with JavaScript off
    private const string Footer =
        "<div style=\"width:100%;font-size:8px;text-align:center;color:#555\"><span class=\"pageNumber\"></span> / <span class=\"totalPages\"></span></div>";

    private readonly SemaphoreSlim _slots = new(options.MaxConcurrency, options.MaxConcurrency);
    private readonly SemaphoreSlim _launch = new(1, 1);
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private long _blockedRequests;

    /// <summary>Requests the pages tried and were aborted since start; templates must not cause any.</summary>
    public long BlockedRequests => Interlocked.Read(ref _blockedRequests);

    public async Task<byte[]> RenderAsync(
        Type templateType, IReadOnlyDictionary<string, object?> parameters, PrintPaper paper, CancellationToken cancellationToken)
    {
        if (!typeof(IComponent).IsAssignableFrom(templateType))
        {
            throw new ArgumentException($"{templateType} is not a Razor component.", nameof(templateType));
        }
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        try
        {
            var html = await RenderHtmlAsync(templateType, parameters);
            return await WithPageAsync(
                async (page, timeoutMs) =>
                {
                    await page.SetContentAsync(html, new() { WaitUntil = WaitUntilState.Load, Timeout = timeoutMs });
                    // A label page is the label size with no margin or page number (MM-BR-917)
                    return await page.PdfAsync(paper.Width is not null
                        ? new()
                        {
                            Width = paper.Width,
                            Height = paper.Height,
                            PrintBackground = true,
                            Margin = new() { Top = "0", Right = "0", Bottom = "0", Left = "0" },
                        }
                        : new()
                        {
                            Format = paper.Format,
                            PrintBackground = true,
                            DisplayHeaderFooter = true,
                            HeaderTemplate = "<span></span>",
                            FooterTemplate = Footer,
                            Margin = new() { Top = PrintPaper.Margin, Right = PrintPaper.Margin, Bottom = PrintPaper.Margin, Left = PrintPaper.Margin },
                        });
                },
                limit.Token);
        }
        // Playwright's own timeouts are already System.TimeoutException; the overall limit cancels the token
        catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            throw TimedOut(e);
        }
    }

    public Task CheckAsync(CancellationToken cancellationToken) =>
        WithPageAsync(
            async (page, timeoutMs) =>
            {
                await page.SetContentAsync("<p>ok</p>", new() { Timeout = timeoutMs });
                return true;
            },
            cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_browser is { IsConnected: true })
        {
            await _browser.CloseAsync();
        }
        _playwright?.Dispose();
        _slots.Dispose();
        _launch.Dispose();
    }

    private System.TimeoutException TimedOut(Exception inner) =>
        new($"PDF conversion took longer than {options.TimeoutSeconds} seconds.", inner);

    private async Task<string> RenderHtmlAsync(Type template, IReadOnlyDictionary<string, object?> parameters)
    {
        await using var renderer = new HtmlRenderer(services, loggerFactory);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync(template, ParameterView.FromDictionary(parameters.ToDictionary()))).ToHtmlString());
        // The font faces are code-made CSS, added after rendering so template values stay HTML encoded
        var head = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        return head < 0 ? PrintFonts.StyleElement + html : html.Insert(head, PrintFonts.StyleElement);
    }

    private async Task<T> WithPageAsync<T>(Func<IPage, float, Task<T>> work, CancellationToken cancellationToken)
    {
        await _slots.WaitAsync(cancellationToken);
        try
        {
            var browser = await BrowserAsync(cancellationToken);
            await using var context = await browser.NewContextAsync(new() { JavaScriptEnabled = false });
            var page = await context.NewPageAsync();
            await page.RouteAsync("**/*", async route =>
            {
                Interlocked.Increment(ref _blockedRequests);
                await route.AbortAsync();
            });
            return await work(page, options.TimeoutSeconds * 1000f).WaitAsync(cancellationToken);
        }
        finally
        {
            _slots.Release();
        }
    }

    private async Task<IBrowser> BrowserAsync(CancellationToken cancellationToken)
    {
        if (_browser is { IsConnected: true })
        {
            return _browser;
        }
        await _launch.WaitAsync(cancellationToken);
        try
        {
            if (_browser is { IsConnected: true })
            {
                return _browser;
            }
            _playwright ??= await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
            return _browser;
        }
        finally
        {
            _launch.Release();
        }
    }
}
