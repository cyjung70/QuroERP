using System.Globalization;
using Bunit;
using FluentAssertions;
using QuroErp.Web.Components.Pages.Samples;
using QuroErp.Web.Components.Screens;

namespace QuroErp.Web.Tests.Screens;

/// <summary>
/// S form fields bound to the form's one-row data set (TASK-CORE-054, qurogrid-integration.md 10).
/// </summary>
public class SingleDataSetTests : ScreenContext
{
    private static QuroSingleForm<SampleItem> Form(IRenderedComponent<SampleSingle> page) =>
        page.FindComponent<QuroSingleForm<SampleItem>>().Instance;

    private static void Search(IRenderedComponent<SampleSingle> page, string code)
    {
        page.Find("[data-testid=single-search-code]").Change(code);
        page.Find("[data-testid=single-toolbar-search]").Click();
    }

    [Fact]
    public void TypedValue_IsSetOnTheDataSetRow_AsAChange()
    {
        var page = Render<SampleSingle>();
        Search(page, "A100");
        Form(page).DataSet.HasChanges.Should().BeFalse();

        page.Find("[data-testid=single-header-name] input").Change("Apricot");

        var data = Form(page).DataSet;
        data.HasChanges.Should().BeTrue();
        data.GetValue(data.CurrentRowKey!.Value, "Name").Should().Be("Apricot");
    }

    [Fact]
    public void NewRecord_Untouched_HasNoChanges()
    {
        var page = Render<SampleSingle>();

        Form(page).DataSet.HasChanges.Should().BeFalse();
        Form(page).IsDirty.Should().BeFalse();
    }

    [Fact]
    public void CodeField_IsUpperCasedInTheDataSet()
    {
        var page = Render<SampleSingle>();

        page.Find("[data-testid=single-header-code] input").Change("c300");

        var data = Form(page).DataSet;
        data.GetValue(data.CurrentRowKey!.Value, "Code").Should().Be("C300");
    }

    [Fact]
    public void LoadedRecord_LocksTheNewOnlyCode_AndKeepsOtherFieldsEditable()
    {
        var page = Render<SampleSingle>();
        page.Find("[data-testid=single-header-code] input").HasAttribute("readonly").Should().BeFalse();

        Search(page, "A100");

        page.Find("[data-testid=single-header-code] input").HasAttribute("readonly").Should().BeTrue();
        page.Find("[data-testid=single-header-code]").Closest(".quro-field")!.ClassList.Should().Contain("quro-field-readonly");
        page.Find("[data-testid=single-header-name] input").HasAttribute("readonly").Should().BeFalse();
    }

    [Theory]
    [InlineData("ko", "2026-01-01")]
    [InlineData("en", "01/01/2026")]
    public void DateField_ShowsTheDateInTheUserLanguage(string lang, string expected)
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(lang);
        var page = Render<SampleSingle>();

        Search(page, "A100");

        page.Find("[data-testid=single-header-valid-from] input").GetAttribute("value").Should().Be(expected);
    }
}
