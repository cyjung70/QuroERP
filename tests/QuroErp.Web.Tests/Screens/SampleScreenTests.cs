using Bunit;
using FluentAssertions;
using QuroErp.Web.Components.Pages.Samples;

namespace QuroErp.Web.Tests.Screens;

/// <summary>
/// The four screen type samples (TASK-CORE-006 "4개 유형 샘플 화면이 동작한다",
/// "저장하지 않은 변경 확인, 행 상태(I/U/D)가 동작한다").
/// </summary>
public class SampleScreenTests : ScreenContext
{
    private const string Unsaved = "저장하지 않은 변경 내용이 있습니다. 계속하면 변경 내용이 사라집니다. 계속할까요?";

    // S

    [Fact]
    public void Single_ToolbarButtons_InFixedOrder()
    {
        var page = Render<SampleSingle>();

        page.FindAll("[role=toolbar] button").Select(b => b.GetAttribute("data-testid"))
            .Should().Equal("sample-single-search", "sample-single-new", "sample-single-save", "sample-single-delete");
    }

    [Fact]
    public void Single_Search_LoadsRecord_AndLocksNewOnlyField()
    {
        var page = Render<SampleSingle>();
        page.Find("[data-testid=sample-single-code]").HasAttribute("readonly").Should().BeFalse();

        page.Find("[data-testid=sample-single-search-code]").Change("a100");
        page.Find("[data-testid=sample-single-search]").Click();

        page.Find("[data-testid=sample-single-name]").GetAttribute("value").Should().Be("Apple");
        page.Find("[data-testid=sample-single-code]").HasAttribute("readonly").Should().BeTrue();
        page.Find("[data-testid=sample-single-delete]").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Single_TextCaseUpper_ConvertsInput()
    {
        var page = Render<SampleSingle>();

        page.Find("[data-testid=sample-single-code]").Change("abc");

        page.Find("[data-testid=sample-single-code]").GetAttribute("value").Should().Be("ABC");
    }

    [Fact]
    public void Single_SaveWithEmptyRequired_ShowsRequiredMessage_AndDoesNotSave()
    {
        var page = Render<SampleSingle>();
        page.Find("[data-testid=sample-single-code]").Change("C300");

        page.Find("[data-testid=sample-single-save]").Click();

        page.Find("[data-testid=sample-single-error]").TextContent.Should().Contain("이름을(를) 입력하세요.");
        Store.FindItem("C300").Should().BeNull();
    }

    [Fact]
    public void Single_SaveNew_StoresRecord()
    {
        var page = Render<SampleSingle>();
        page.Find("[data-testid=sample-single-code]").Change("c300");
        page.Find("[data-testid=sample-single-name]").Change("Cherry");

        page.Find("[data-testid=sample-single-save]").Click();

        Store.FindItem("C300")!.Name.Should().Be("Cherry");
        page.Find("[data-testid=sample-single-notice]").TextContent.Should().Contain("저장했습니다.");
        page.Find("[data-testid=sample-single-code]").HasAttribute("readonly").Should().BeTrue();
    }

    [Fact]
    public void Single_NewWithUnsavedChanges_AsksAndKeepsInputWhenCancelled()
    {
        var page = Render<SampleSingle>();
        page.Find("[data-testid=sample-single-name]").Change("Draft");
        Confirm.Answer = false;

        page.Find("[data-testid=sample-single-new]").Click();

        Confirm.Questions.Should().Equal(Unsaved);
        page.Find("[data-testid=sample-single-name]").GetAttribute("value").Should().Be("Draft");
    }

    [Fact]
    public void Single_NewWithoutChanges_DoesNotAsk()
    {
        var page = Render<SampleSingle>();

        page.Find("[data-testid=sample-single-new]").Click();

        Confirm.Questions.Should().BeEmpty();
    }

    [Fact]
    public void Single_Delete_AsksThenDeletes()
    {
        var page = Render<SampleSingle>();
        page.Find("[data-testid=sample-single-search-code]").Change("A100");
        page.Find("[data-testid=sample-single-search]").Click();

        page.Find("[data-testid=sample-single-delete]").Click();

        Confirm.Questions.Should().Equal("삭제할까요?");
        Store.FindItem("A100").Should().BeNull();
    }

    // M

    [Fact]
    public void Multi_LoadsRows_WithToolbarInFixedOrder()
    {
        var page = Render<SampleMulti>();

        page.WaitForAssertion(() => page.FindAll("[data-testid=name]").Should().HaveCount(2));
        page.FindAll("[role=toolbar] button").Select(b => b.GetAttribute("data-testid"))
            .Should().Equal("sample-multi-search", "sample-multi-new", "sample-multi-save", "sample-multi-export");
    }

    [Fact]
    public void Multi_RowStates_AreShownAsIUD()
    {
        var page = Render<SampleMulti>();
        page.WaitForAssertion(() => page.FindAll("[data-testid=name]").Should().HaveCount(2));

        page.FindAll("[data-testid=name]")[0].Change("Apricot");
        page.FindAll("[data-testid=delete-row]")[1].Click();
        page.Find("[data-testid=sample-multi-new]").Click();

        page.FindAll("[data-state]").Select(s => s.GetAttribute("data-state")).Should().Equal("U", "D", "I");
    }

    [Fact]
    public void Multi_Save_SendsBatch_AndReloads()
    {
        var page = Render<SampleMulti>();
        page.WaitForAssertion(() => page.FindAll("[data-testid=name]").Should().HaveCount(2));
        page.FindAll("[data-testid=name]")[0].Change("Apricot");
        page.FindAll("[data-testid=delete-row]")[1].Click();
        page.Find("[data-testid=sample-multi-new]").Click();
        page.FindAll("[data-testid=code]")[2].Change("c300");
        page.FindAll("[data-testid=name]")[2].Change("Cherry");

        page.Find("[data-testid=sample-multi-save]").Click();

        Store.Items().Select(i => (i.Code, i.Name)).Should().Equal(("A100", "Apricot"), ("C300", "Cherry"));
        page.FindAll("[data-testid=name]").Should().HaveCount(2);
        page.FindAll("[data-state]").Should().BeEmpty();
    }

    [Fact]
    public void Multi_SaveWithEmptyRequiredCell_ShowsRequiredMessage()
    {
        var page = Render<SampleMulti>();
        page.WaitForAssertion(() => page.FindAll("[data-testid=name]").Should().HaveCount(2));
        page.Find("[data-testid=sample-multi-new]").Click();

        page.Find("[data-testid=sample-multi-save]").Click();

        page.Find("[data-testid=sample-multi-error]").TextContent.Should().Contain("을(를) 입력하세요.");
        Store.Items().Should().HaveCount(2);
    }

    [Fact]
    public void Multi_SaveWithoutChanges_SaysNoChanges()
    {
        var page = Render<SampleMulti>();
        page.WaitForAssertion(() => page.FindAll("[data-testid=name]").Should().HaveCount(2));

        page.Find("[data-testid=sample-multi-save]").Click();

        page.Find("[data-testid=sample-multi-notice]").TextContent.Should().Contain("저장할 변경 내용이 없습니다.");
    }

    [Fact]
    public void Multi_SearchWithUnsavedChanges_Asks()
    {
        var page = Render<SampleMulti>();
        page.WaitForAssertion(() => page.FindAll("[data-testid=name]").Should().HaveCount(2));
        page.FindAll("[data-testid=name]")[0].Change("Apricot");
        Confirm.Answer = false;

        page.Find("[data-testid=sample-multi-search]").Click();

        Confirm.Questions.Should().Equal(Unsaved);
        page.Find("[data-state=U]").Should().NotBeNull();
    }

    // SM

    [Fact]
    public void HeaderDetail_SaveHeaderAndLines_InOneSave()
    {
        var page = Render<SampleHeaderDetail>();
        page.Find("[data-testid=sample-sm-search-no]").Change("SO261000001");
        page.Find("[data-testid=sample-sm-search]").Click();
        page.WaitForAssertion(() => page.FindAll("[data-testid=item]").Should().HaveCount(2));

        page.FindAll("[data-testid=delete-row]")[0].Click();
        page.Find("[data-testid=sample-sm-lines-add-row]").Click();
        page.FindAll("[data-testid=item]")[2].Change("c300");
        page.Find("[data-testid=sample-sm-save]").Click();

        Store.FindOrder("SO261000001")!.Value.Lines.Select(l => l.ItemCode).Should().Equal("B200", "C300");
        page.FindAll("[data-testid=item]").Should().HaveCount(2);
    }

    [Fact]
    public void HeaderDetail_LineChangeOnly_IsUnsavedChange()
    {
        var page = Render<SampleHeaderDetail>();
        page.Find("[data-testid=sample-sm-search-no]").Change("SO261000001");
        page.Find("[data-testid=sample-sm-search]").Click();
        page.WaitForAssertion(() => page.FindAll("[data-testid=item]").Should().HaveCount(2));
        page.FindAll("[data-testid=qty]")[0].Change("9");
        Confirm.Answer = false;

        page.Find("[data-testid=sample-sm-new]").Click();

        Confirm.Questions.Should().Equal(Unsaved);
    }

    // MM

    [Fact]
    public void MasterDetail_SelectSource_LoadsTargets_AndAsksWhenTargetsChanged()
    {
        var page = Render<SampleMasterDetail>();
        page.WaitForAssertion(() => page.FindAll("[data-testid=select-row]").Should().HaveCount(2));

        page.FindAll("[data-testid=select-row]")[0].Click();
        page.FindAll("[data-testid=item]").Should().ContainSingle();

        page.FindAll("[data-testid=qty]")[0].Change("5");
        Confirm.Answer = false;
        page.FindAll("[data-testid=select-row]")[1].Click();

        Confirm.Questions.Should().Equal(Unsaved);
        page.FindAll("[data-testid=item]").Should().ContainSingle();
    }

    [Fact]
    public void MasterDetail_SaveTargets_StoresForSelectedSource()
    {
        var page = Render<SampleMasterDetail>();
        page.WaitForAssertion(() => page.FindAll("[data-testid=select-row]").Should().HaveCount(2));
        page.FindAll("[data-testid=select-row]")[1].Click();

        page.Find("[data-testid=sample-mm-new]").Click();
        page.FindAll("[data-testid=item]")[0].Change("b200");
        page.Find("[data-testid=sample-mm-save]").Click();

        Store.Members(2).Should().ContainSingle(m => m.ItemCode == "B200");
    }
}
