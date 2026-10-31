using System.Text.RegularExpressions;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using QuroErp.Web.Components.Pages.Samples;
using QuroErp.Web.Components.Screens;

namespace QuroErp.Web.Tests.Screens;

/// <summary>Screen type F: optional title bar, one message line, a free body (TASK-CORE-043, screen-templates.md 5).</summary>
public partial class FreeFormTests : ScreenContext
{
    private static readonly RenderFragment Body = b =>
    {
        b.OpenElement(0, "p");
        b.AddAttribute(1, "data-testid", "body");
        b.AddContent(2, "Body");
        b.CloseElement();
    };

    [Fact, Trait("Id", "CORE-043-WT-01")]
    public void WithoutTitle_HasNoTitleBar_ButShowsTheBody()
    {
        var page = Render<QuroFreeForm>(p => p.Add(x => x.TestId, "free").Add(x => x.ChildContent, Body));

        page.FindAll("[data-testid=free-title], header, h1").Should().BeEmpty();
        page.Find("[data-testid=body]").TextContent.Should().Be("Body");
        page.FindAll("[data-testid=free-message]").Should().BeEmpty("there is no error or notice");
    }

    [Fact, Trait("Id", "CORE-043-WT-02")]
    public void WithTitle_ShowsTheTitleBar_AndNoCommandBarSearchOrAudit()
    {
        var page = Render<QuroFreeForm>(p => p.Add(x => x.TestId, "free").Add(x => x.Title, "Home").Add(x => x.Path, "Menu")
            .Add(x => x.ChildContent, Body));

        page.Find("[data-testid=free-title] h1").TextContent.Should().Be("Home");
        page.Find("[data-testid=free-title] .quro-entry-path").TextContent.Should().Be("Menu › Home");
        page.FindAll("[role=toolbar], [role=search], [data-testid=free-audit], [data-testid=free-toolbar]").Should().BeEmpty();
    }

    [Theory, Trait("Id", "CORE-043-WT-03")]
    [InlineData("Failed", null, "alert", "error")]
    [InlineData(null, "Done", "status", "info")]
    [InlineData("Failed", "Done", "alert", "error")]
    public void Message_ShowsOneLine_ErrorBeforeNotice(string? error, string? notice, string role, string level)
    {
        var page = Render<QuroFreeForm>(p => p.Add(x => x.TestId, "free").Add(x => x.Error, error).Add(x => x.Notice, notice));

        var message = page.FindAll("[data-testid=free-message]").Should().ContainSingle().Subject;
        message.GetAttribute("role").Should().Be(role);
        message.GetAttribute("data-level").Should().Be(level);
        message.TextContent.Should().Be(error ?? notice);
    }

    [Fact, Trait("Id", "CORE-043-WT-04")]
    public void Sample_ShowsTitleCardsAndFlow_WithCommonClasses_AndANotice()
    {
        var page = Render<SampleFreeForm>();

        page.Find("[data-testid=free-form-title] h1").TextContent.Should().Be("샘플: 자유 배치 (F)");
        page.FindAll(".quro-free-grid > .quro-panel").Should().HaveCount(3);
        page.FindAll(".quro-free-grid .quro-chip").Should().HaveCount(3);
        page.FindAll("[data-testid=free-form-flow] li").Select(s => s.TextContent).Should().Equal("발주", "입고", "공급자 송장");
        page.FindAll("button").Should().OnlyContain(b => b.ClassList.Contains("quro-btn"))
            .And.OnlyContain(b => TestId().IsMatch(b.GetAttribute("data-testid") ?? ""));

        page.Find("[data-testid=free-form-show-notice]").Click();

        page.Find("[data-testid=free-form-message]").TextContent.Should().Be("F 화면은 메시지 한 줄을 보입니다.");
    }

    [Fact, Trait("Id", "CORE-043-WT-05")]
    public void Css_StacksTheFreeGridIntoOneColumnBelow768px()
    {
        var css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "QuroErp.Web", "wwwroot", "css", "quro-entry.css"));

        var mobile = css[css.IndexOf("@media (max-width: 767.98px)", StringComparison.Ordinal)..];
        mobile.Should().Contain(".quro-free-grid { grid-template-columns: 1fr; }");
    }

    [GeneratedRegex("^free-form-[a-z0-9-]+$")]
    private static partial Regex TestId();
}
