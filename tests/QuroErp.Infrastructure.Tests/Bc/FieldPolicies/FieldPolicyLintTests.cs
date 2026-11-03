using System.Xml.Linq;
using FluentAssertions;
using QuroErp.Domain.Bc.FieldPolicies;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Infrastructure.Bc.FieldPolicies;

namespace QuroErp.Infrastructure.Tests.Bc.FieldPolicies;

/// <summary>
/// Country pack lint (BC-BR-437, field-policy.md "검증"): the built-in packs are checked against the field catalog and the Web
/// label resources (SharedResource.resx and .en.resx) before they ship.
/// </summary>
public class FieldPolicyLintTests
{
    private static readonly IReadOnlyList<CountryPack> Packs = CountryPackReader.ReadEmbedded();

    public static TheoryData<string> Countries => new(Packs.Select(p => p.CountryCode));

    [Fact, Trait("Id", "BC-UT-437"), Trait("Rule", "BC-BR-437")]
    public void EverySupportedCountry_HasAPack()
    {
        Packs.Select(p => p.CountryCode).Should().BeEquivalentTo(CountryProfiles.Supported);
    }

    [Theory, Trait("Id", "BC-UT-437"), Trait("Rule", "BC-BR-437")]
    [MemberData(nameof(Countries))]
    public void Pack_HasNoProblems(string country)
    {
        FieldPolicyRules.CheckPack(Pack(country)).Should().BeEmpty();
    }

    [Theory, Trait("Id", "BC-UT-437"), Trait("Rule", "BC-BR-437")]
    [MemberData(nameof(Countries))]
    public void Pack_EveryFieldKey_IsALabelResourceInKoAndEn(string country)
    {
        var pack = Pack(country);
        var keys = pack.Policies.Keys.Concat(pack.Defaults.Keys).Where(k => !FieldCatalog.IsExtKey(k)).Distinct();

        foreach (var file in new[] { "SharedResource.resx", "SharedResource.en.resx" })
        {
            keys.Except(ResourceKeys(file)).Should().BeEmpty($"every field key of the {country} pack is a label in {file}");
        }
    }

    [Theory, Trait("Id", "BC-UT-437"), Trait("Rule", "BC-BR-437")]
    [MemberData(nameof(Countries))]
    public void Pack_HasALabelFileForEachLanguage(string country)
    {
        Pack(country).Labels.Keys.Should().BeEquivalentTo("en", "ko");
    }

    [Fact, Trait("Id", "BC-UT-437"), Trait("Rule", "BC-BR-437")]
    public void Packs_NameTheSameFields()
    {
        // A field one country sets and another leaves out falls back to OPTIONAL there, which is rarely meant
        Packs.Select(p => p.Policies.Keys.Concat(p.Defaults.Keys).ToHashSet())
            .Should().AllSatisfy(keys => keys.Should().BeEquivalentTo(Packs[0].Policies.Keys.Concat(Packs[0].Defaults.Keys).ToHashSet()));
    }

    [Fact, Trait("Id", "BC-UT-437"), Trait("Rule", "BC-BR-437")]
    public void Lint_FailsForABadPack()
    {
        var pack = CountryPackReader.Parse("KR", new Dictionary<string, string>
        {
            [CountryPackReader.FieldPolicyFile] = "Sd.Customer.CName: HIDDEN\nSd.Customer.Typo: OPTIONAL\n",
            [CountryPackReader.DefaultsFile] = "Sd.Customer.Country: JP\n",
        });

        FieldPolicyRules.CheckPack(pack).Should().HaveCount(3);
    }

    private static CountryPack Pack(string country) => Packs.Single(p => p.CountryCode == country);

    private static HashSet<string> ResourceKeys(string file) =>
        XDocument.Load(Path.Combine(RepositoryRoot(), "src", "QuroErp.Web", "Resources", file))
            .Root!.Elements("data").Select(e => e.Attribute("name")!.Value).ToHashSet(StringComparer.Ordinal);

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "QuroErp.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("QuroErp.slnx was not found.");
    }
}
