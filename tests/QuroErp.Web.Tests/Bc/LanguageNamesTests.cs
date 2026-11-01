using FluentAssertions;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Pages.Bc.Models;

namespace QuroErp.Web.Tests.Bc;

/// <summary>Name columns per language on the package grid (TASK-CORE-057).</summary>
public class LanguageNamesTests
{
    [Fact, Trait("Rule", "BC-BR-405")]
    public void Set_EmptyNameOfALanguageNotInUse_AddsNoKey()
    {
        var names = new Dictionary<string, string?> { ["en"] = "Yen" };

        LanguageNames.Set(names, "ko", null);

        names.Should().NotContainKey("ko");
    }

    [Fact]
    public void Set_ChangesTheNameOfALanguageInUse_AlsoToEmpty()
    {
        var names = new Dictionary<string, string?> { ["en"] = "Yen", ["ko"] = "엔" };

        LanguageNames.Set(names, "ko", null);
        LanguageNames.Set(names, "en", "Japanese Yen");

        names.Should().Equal(new Dictionary<string, string?> { ["en"] = "Japanese Yen", ["ko"] = null });
    }

    [Fact]
    public void RowProperties_ReadAndWriteTheNames()
    {
        var row = CurrencyRow.New(["en", "ko"]);

        row.NameEn = "Euro";

        row.Names["en"].Should().Be("Euro");
        row.NameKo.Should().BeNull();
    }

    [Fact]
    public void InUse_HidesTheNameColumnsOfLanguagesNotInUse()
    {
        var grid = ScreenDefinitions.Shared.Get("bc.currency.list").Grids[0];

        var adjusted = LanguageNames.InUse(grid, ["en"]);

        adjusted.Columns.Single(c => c.Id == "nameKo").Hidden.Should().BeTrue();
        adjusted.Columns.Single(c => c.Id == "nameEn").Should().BeEquivalentTo(grid.Columns.Single(c => c.Id == "nameEn"));
        grid.Columns.Single(c => c.Id == "nameKo").Hidden.Should().BeFalse("the shared definition does not change");
    }
}
