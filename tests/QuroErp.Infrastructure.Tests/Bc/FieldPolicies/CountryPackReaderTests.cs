using FluentAssertions;
using QuroErp.Domain.Bc.FieldPolicies;
using QuroErp.Infrastructure.Bc.FieldPolicies;

namespace QuroErp.Infrastructure.Tests.Bc.FieldPolicies;

/// <summary>Reading country pack YAML (field-policy.md "국가팩").</summary>
public class CountryPackReaderTests
{
    [Fact, Trait("Id", "BC-UT-431")]
    public void Parse_ReadsStates_Mappings_Defaults_AndLabels()
    {
        var pack = CountryPackReader.Parse("KR", new Dictionary<string, string>
        {
            [CountryPackReader.FieldPolicyFile] = """
                Sd.Customer.State: HIDDEN
                Sd.Customer.BusinessRegNo:
                  state: OPTIONAL
                  label: Kr.Label.BusinessRegNo
                  default: "000"
                """,
            [CountryPackReader.DefaultsFile] = "Sd.Customer.Country: KR\n",
            ["labels.ko.yaml"] = "Kr.Label.BusinessRegNo: 사업자등록번호\n",
            ["labels.en.yaml"] = "{}\n",
        });

        pack.Policies.Should().Equal(new Dictionary<string, FieldPolicyEntry>
        {
            ["Sd.Customer.State"] = new("HIDDEN", null, null),
            ["Sd.Customer.BusinessRegNo"] = new("OPTIONAL", "000", "Kr.Label.BusinessRegNo"),
        });
        pack.Defaults.Should().Equal(new Dictionary<string, string> { ["Sd.Customer.Country"] = "KR" });
        pack.Labels["ko"].Should().Equal(new Dictionary<string, string> { ["Kr.Label.BusinessRegNo"] = "사업자등록번호" });
        pack.Labels["en"].Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UT-431")]
    public void Parse_MissingOrEmptyFiles_AreEmpty()
    {
        var pack = CountryPackReader.Parse("US", new Dictionary<string, string> { [CountryPackReader.DefaultsFile] = "# none\n" });

        (pack.Policies.Count, pack.Defaults.Count, pack.Labels.Count).Should().Be((0, 0, 0));
    }

    [Theory, Trait("Id", "BC-UT-437"), Trait("Rule", "BC-BR-437")]
    [InlineData("- a list\n")]
    [InlineData("Sd.Customer.State:\n  hidden: true\n")]
    [InlineData("Sd.Customer.State: [HIDDEN]\n")]
    [InlineData("Sd.Customer.State: \"\"\n")]
    public void Parse_AnUnexpectedShape_NamesTheFileAndKey(string yaml)
    {
        var act = () => CountryPackReader.Parse("US", new Dictionary<string, string> { [CountryPackReader.FieldPolicyFile] = yaml });

        act.Should().Throw<InvalidDataException>().WithMessage("countrypacks/US/field-policy.yaml *");
    }
}
