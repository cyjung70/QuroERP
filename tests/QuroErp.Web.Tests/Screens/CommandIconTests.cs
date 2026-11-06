using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using QuroErp.Web.Components.Pages.Samples;
using QuroErp.Web.Components.Screens;

namespace QuroErp.Web.Tests.Screens;

/// <summary>Standard commands show a decorative icon before their text; other buttons stay text-only (screens.md "버튼 아이콘").</summary>
public class CommandIconTests : ScreenContext
{
    private static IElement Icon(IElement button) => button.QuerySelector(".quro-command-icon")!;

    private IRenderedComponent<QuroToolbar> Toolbar(Action<ComponentParameterCollectionBuilder<QuroToolbar>>? more = null)
    {
        var handler = EventCallback.Factory.Create(this, () => { });
        return Render<QuroToolbar>(p =>
        {
            p.Add(x => x.TestId, "t")
                .Add(x => x.OnSearch, handler).Add(x => x.OnPrevious, handler).Add(x => x.OnNext, handler)
                .Add(x => x.OnNew, handler).Add(x => x.OnSave, handler).Add(x => x.OnDelete, handler)
                .Add(x => x.OnPrint, handler).Add(x => x.OnExport, handler);
            more?.Invoke(p);
        });
    }

    [Theory]
    [InlineData("search", "search", "조회")]
    [InlineData("previous", "previous", "이전")]
    [InlineData("next", "next", "다음")]
    [InlineData("new", "new", "신규")]
    [InlineData("save", "save", "저장")]
    [InlineData("delete", "delete", "삭제")]
    [InlineData("print", "print", "인쇄")]
    [InlineData("export", "excel", "엑셀")]
    public void Toolbar_StandardButton_HasItsIconAndKeepsItsText(string id, string icon, string text)
    {
        var button = Toolbar().Find($"[data-testid=t-toolbar-{id}]");

        Icon(button).GetAttribute("data-icon").Should().Be(icon);
        button.FirstElementChild.Should().BeSameAs(Icon(button));
        button.TextContent.Should().Be(text);
    }

    [Fact]
    public void Toolbar_ShowAllFieldsAndScreenCommands_StayTextOnly()
    {
        var page = Toolbar(p => p
            .Add(x => x.CanShowAllFields, true)
            .Add(x => x.ExtraCommands, (RenderFragment)(b => b.AddMarkupContent(0, "<button data-testid=\"t-own\">Own</button>"))));

        page.Find("[data-testid=t-toolbar-show-all-fields]").QuerySelector("img").Should().BeNull();
        page.Find("[data-testid=t-own]").QuerySelector("img").Should().BeNull();
    }

    [Theory]
    [InlineData("search", "search")]
    [InlineData("new", "new")]
    [InlineData("save", "save")]
    [InlineData("add-row", "add-row")]
    [InlineData("mark-delete", "mark-delete")]
    [InlineData("restore-row", "restore-line")]
    [InlineData("delete", "delete")]
    public void EntryFrame_StandardButton_HasItsIcon(string id, string icon)
    {
        var page = Render<SamplePoEntry>();

        Icon(page.Find($"[data-testid=po-entry-toolbar-{id}]")).GetAttribute("data-icon").Should().Be(icon);
    }

    [Fact]
    public void EntryFrame_StatusCommand_StaysTextOnly()
    {
        var page = Render<SamplePoEntry>(p => p.Add(x => x.PoNo, "PO261000001"));

        page.WaitForAssertion(() => page.Find("[data-testid=po-entry-toolbar-approve]").QuerySelector("img").Should().BeNull());
    }

    [Theory]
    [InlineData("search", "disabled", "normal", "hover")]
    [InlineData("new", "disabled", "normal", "hover")]
    [InlineData("delete", "disabled", "normal", "hover")]
    [InlineData("add-row", "disabled", "normal", "hover")]
    [InlineData("mark-delete", "disabled", "normal", "hover")]
    [InlineData("next", "disabled", "normal", "hover")]
    [InlineData("previous", "disabled", "normal", "hover")]
    [InlineData("excel", "disabled", "normal", "hover")]
    [InlineData("print", "disabled", "normal", "hover")]
    [InlineData("save", "disabled", "outline")]
    [InlineData("restore-line", "disabled", "normal", "hover", "outline")]
    public void Icon_RendersEachStateAsDecoration_FromAFileThatExists(string name, params string[] states)
    {
        var images = Render<QuroCommandIcon>(p => p.Add(x => x.Name, name)).FindAll("img");

        images.Select(i => i.GetAttribute("data-state")).Should().Equal(states);
        images.Should().OnlyContain(i => i.GetAttribute("alt") == "");
        foreach (var image in images)
        {
            image.GetAttribute("src").Should().Be($"img/commands/{name}-{image.GetAttribute("data-state")}.png");
            File.Exists(Path.Combine(WebRoot(), image.GetAttribute("src")!)).Should().BeTrue(image.GetAttribute("src"));
        }
    }

    private static string WebRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "QuroErp.slnx")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("QuroErp.slnx was not found."), "src", "QuroErp.Web", "wwwroot");
    }
}
