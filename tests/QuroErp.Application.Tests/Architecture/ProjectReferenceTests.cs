using System.Xml.Linq;
using FluentAssertions;

namespace QuroErp.Application.Tests.Architecture;

/// <summary>
/// 프로젝트 참조가 docs/architecture/layers.md "프로젝트 참조 (단방향)" 표와 같은지 검사한다.
/// </summary>
public class ProjectReferenceTests
{
    public static TheoryData<string, string[]> AllowedReferences => new()
    {
        { "QuroErp.Web", ["QuroErp.Contracts"] },
        { "QuroErp.Api", ["QuroErp.Contracts", "QuroErp.Application", "QuroErp.Infrastructure"] },
        { "QuroErp.Application", ["QuroErp.Domain", "QuroErp.Contracts"] },
        { "QuroErp.Domain", [] },
        { "QuroErp.Infrastructure", ["QuroErp.Application", "QuroErp.Domain"] },
        { "QuroErp.Contracts", [] },
    };

    [Theory, Trait("Id", "CORE-001-T01")]
    [MemberData(nameof(AllowedReferences))]
    public void ProjectReferences_MatchLayersTable(string project, string[] expected)
    {
        ProjectReferencesOf(project).Should().BeEquivalentTo(expected);
    }

    [Fact, Trait("Id", "CORE-001-T02")]
    public void SourceProjects_AreAllCoveredByLayersTable()
    {
        var projects = Directory.GetDirectories(Path.Combine(RepositoryRoot(), "src")).Select(Path.GetFileName);

        projects.Should().BeEquivalentTo(AllowedReferences.Select(row => (string)row[0]));
    }

    private static IEnumerable<string> ProjectReferencesOf(string project)
    {
        var csproj = Path.Combine(RepositoryRoot(), "src", project, $"{project}.csproj");
        return XDocument.Load(csproj)
            .Descendants("ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension(e.Attribute("Include")!.Value.Replace('\\', '/')));
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "QuroErp.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("QuroErp.slnx를 찾지 못했다.");
    }
}
