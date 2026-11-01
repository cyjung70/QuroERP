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

    private static ScreenDefinitions Load(string text) => ScreenDefinitions.Load(
        [("screens/samples/samples.item.multi.screen.toon", text)],
        screens => new ErpDefinitionCatalog(
            ErpDefinitionCatalog.SharedResourceKeys(), ScreenDefinitions.CodeMajorsOf(screens), new HashSet<string>(), new HashSet<string>(),
            ErpDefinitionCatalog.KnownPermissions));

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
