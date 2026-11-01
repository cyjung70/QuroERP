using System.Text.RegularExpressions;
using FluentAssertions;
using QuroErp.Web.Common;

namespace QuroErp.Web.Tests.Definitions;

/// <summary>
/// The screen definitions load and pass the catalog check (TASK-CORE-055, qurogrid-integration.md 5).
/// Run with <c>dotnet test --filter Category=ScreenDefinition</c>.
/// </summary>
[Trait("Category", "ScreenDefinition")]
public partial class ScreenDefinitionTests
{
    private const string Grid = """
        screen:
          id: samples.item.multi
          module: SAMPLES
          title: TITLE
          version: 1
          api: /api/samples/items
          key: id
          layout: grid
          perm:
            read: SAMPLES.Read
            write: SAMPLES.Write
        toolbar[2]: query,save
        form:
          cols: 1
          fields[1]{id,label,type,mode,max,case,default,source,span,on}:
            id,"",hidden,ro,"","","","",0,""
        grids[1]:
          - id: items
            bind: items
            rowKey: id
            columns[1]{id,label,type,width,edit,req,fmt,lookup,sum,hidden}:
              code,Samples.Code,text,120,new,true,"","",false,false
        """;

    private const string ItemLookup = """
        lookup:
          id: sampleItem
          title: TITLE
          version: 1
          api: /api/samples/items/lookup
          key: code
          text: name
          minChars: 1
        queries[1]{id,params}:
          all,""
        columns[2]{id,label,type,width,fmt}:
          code,Samples.Code,text,120,""
          name,Samples.Name,text,220,""
        """;

    [GeneratedRegex(@"\('(?<major>[A-Z0-9_]+)', 'system', 'system'\)")]
    private static partial Regex SeedMajor();

    [Fact]
    public void Shared_LoadsEveryDefinitionFile_WithoutErrors()
    {
        var files = Directory.GetFiles(DefinitionsFolder(), "*.screen.toon", SearchOption.AllDirectories);

        files.Should().NotBeEmpty();
        foreach (var file in files)
        {
            var id = Path.GetFileName(file)[..^".screen.toon".Length];
            ScreenDefinitions.Shared.Get(id).Id.Should().Be(id);
        }
    }

    [Fact]
    public void Shared_LoadsEveryLookupFile_WithoutErrors()
    {
        var files = Directory.GetFiles(DefinitionsFolder(), "*.lookup.toon", SearchOption.AllDirectories);

        files.Should().NotBeEmpty();
        foreach (var file in files)
        {
            var id = Path.GetFileName(file)[..^".lookup.toon".Length];
            ScreenDefinitions.Shared.Lookup(id).Id.Should().Be(id);
        }
    }

    [Fact]
    public void SamplePoEntry_LinesHaveTheLookupRuleAndTheAmountRule()
    {
        var lines = ScreenDefinitions.Shared.Get("samples.po.entry").Grids.Should().ContainSingle().Subject;

        lines.Columns.Single(c => c.Id == "itemCode").Lookup.Should().Be("samplePoItem");
        lines.Rules.Select(r => (r.On, r.Action)).Should().Equal(
            ("itemCode.change", Quro.Grid.Definition.RuleAction.Lookup),
            ("qty.change", Quro.Grid.Definition.RuleAction.Calc),
            ("fob.change", Quro.Grid.Definition.RuleAction.Calc));
    }

    [Fact]
    public void Load_ColumnLookupWithoutItsFile_StopsWithIt()
    {
        var text = Def("Samples.Multi.Title").Replace("text,120,new,true,\"\",\"\"", "text,120,new,true,\"\",sampleItem", StringComparison.Ordinal);

        var act = () => Load(text);

        act.Should().Throw<InvalidOperationException>().WithMessage("*V-37*sampleItem*");
    }

    [Fact]
    public void Load_ColumnLookupWithItsFile_Passes()
    {
        var text = Def("Samples.Multi.Title").Replace("text,120,new,true,\"\",\"\"", "text,120,new,true,\"\",sampleItem", StringComparison.Ordinal);

        var definitions = Load(text, ItemLookup.Replace("TITLE", "Samples.FindItem", StringComparison.Ordinal));

        definitions.Lookup("sampleItem").Key.Should().Be("code");
    }

    [Fact]
    public void Load_LookupTitleNotInTheResourceFiles_ReportsFileAndLine()
    {
        var act = () => Load(Def("Samples.Multi.Title"), ItemLookup.Replace("TITLE", "Samples.NoSuchTitle", StringComparison.Ordinal));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*lookups/samples/sampleItem.lookup.toon:*: V-34 title 'Samples.NoSuchTitle' is not in the resource files*");
    }

    [Fact]
    public void SampleMulti_HasTheGridColumns()
    {
        ScreenDefinitions.Shared.Get("samples.item.multi").Grids.Should().ContainSingle()
            .Which.Columns.Select(c => c.Id).Should().Equal("code", "name", "validFrom", "qty");
    }

    [Fact]
    public void CodeMajors_OfTheDefinitions_AreInTheCodeSeed()
    {
        var seed = File.ReadAllText(Path.Combine(RepoRoot(), "src", "QuroERP.Database", "Scripts", "Seed", "0500_seed_sys_code.sql"));
        var majors = SeedMajor().Matches(seed).Select(m => m.Groups["major"].Value).ToHashSet();
        var screens = Directory.GetFiles(DefinitionsFolder(), "*.screen.toon", SearchOption.AllDirectories)
            .Select(f => ScreenDefinitions.Shared.Get(Path.GetFileName(f)[..^".screen.toon".Length]));

        majors.Should().NotBeEmpty();
        ScreenDefinitions.CodeMajorsOf(screens).Should().BeSubsetOf(majors);
    }

    [Fact]
    public void Load_PascalCaseKeyOfTheResourceFiles_Passes()
    {
        var definitions = Load(Def("Samples.Multi.Title"));

        definitions.Get("samples.item.multi").Title.Should().Be("Samples.Multi.Title");
    }

    [Fact]
    public void Load_KeyNotInTheResourceFiles_ReportsFileAndLine()
    {
        var act = () => Load(Def("Samples.NoSuchTitle"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*screens/samples/samples.item.multi.screen.toon:*: V-21 title 'Samples.NoSuchTitle' is not in the resource files*");
    }

    [Fact]
    public void Load_StructureError_StopsWithIt()
    {
        var text = Def("Samples.Multi.Title").Replace("key: id", "key: code", StringComparison.Ordinal);

        var act = () => Load(text);

        act.Should().Throw<InvalidOperationException>().WithMessage("*V-10*");
    }

    [Fact]
    public void Load_PermissionNotKnown_StopsWithIt()
    {
        var text = Def("Samples.Multi.Title")
            .Replace("write: SAMPLES.Write", "write: SAMPLES.Admin", StringComparison.Ordinal);

        var act = () => Load(text);

        act.Should().Throw<InvalidOperationException>().WithMessage("*V-24*SAMPLES.Admin*");
    }

    private static string Def(string title) => Grid.Replace("TITLE", title, StringComparison.Ordinal);

    private static ScreenDefinitions Load(string text, string? lookup = null) => ScreenDefinitions.Load(
        lookup is null
            ? [("screens/samples/samples.item.multi.screen.toon", text)]
            : [("screens/samples/samples.item.multi.screen.toon", text), ("lookups/samples/sampleItem.lookup.toon", lookup)],
        (screens, lookups) => new ErpDefinitionCatalog(
            ErpDefinitionCatalog.SharedResourceKeys(), ScreenDefinitions.CodeMajorsOf(screens), lookups.Select(l => l.Id).ToHashSet(),
            new HashSet<string>(), ErpDefinitionCatalog.KnownPermissions));

    private static string DefinitionsFolder() => Path.Combine(RepoRoot(), "src", "QuroErp.Web", "Definitions");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "QuroErp.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("QuroErp.slnx was not found.");
    }
}
