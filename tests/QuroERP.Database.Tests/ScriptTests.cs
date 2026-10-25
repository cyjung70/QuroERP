using System.Text.RegularExpressions;
using FluentAssertions;

namespace QuroERP.Database.Tests;

/// <summary>스크립트 파일 이름·번호대 규칙 (database.md "마이그레이션"). DB 없이 돈다.</summary>
public partial class ScriptTests
{
    private static readonly string[] Folders = ["Schema", "Seed", "Demo"];

    [Theory, Trait("Id", "DB-T01")]
    [InlineData("Schema")]
    [InlineData("Seed")]
    public void ScriptNames_FollowNamingRule(string folder)
    {
        DatabaseMigrator.ScriptNames(folder).Should().NotBeEmpty()
            .And.OnlyContain(name => ScriptName().IsMatch(name));
    }

    [Fact, Trait("Id", "DB-T02")]
    public void SchemaScripts_AreBelow0500_AndSeedScriptsFrom0500()
    {
        DatabaseMigrator.ScriptNames("Schema").Select(Number).Should().OnlyContain(n => n >= 1 && n < 500);
        DatabaseMigrator.ScriptNames("Seed").Select(Number).Should().OnlyContain(n => n >= 500);
    }

    [Fact, Trait("Id", "DB-T03")]
    public void ScriptNumbers_AreUnique()
    {
        var numbers = Folders.SelectMany(DatabaseMigrator.ScriptNames).Select(Number);

        numbers.Should().OnlyHaveUniqueItems();
    }

    private static int Number(string name) => int.Parse(name[..4], System.Globalization.CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^\d{4}_[a-z0-9_]+\.sql$")]
    private static partial Regex ScriptName();
}
