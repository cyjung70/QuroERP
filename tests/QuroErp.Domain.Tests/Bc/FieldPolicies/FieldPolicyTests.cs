using FluentAssertions;
using QuroErp.Domain.Bc.FieldPolicies;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Tests.Bc.FieldPolicies;

/// <summary>Merging the three layers and checking company overrides (BC-BR-430, BC-BR-431, BC-BR-433, BC-BR-437).</summary>
public class FieldPolicyTests
{
    private const string Screen = "Sd.Customer";

    private static readonly IReadOnlyDictionary<string, FieldPolicyEntry> NoOverrides = new Dictionary<string, FieldPolicyEntry>();

    private static readonly CountryPack Us = Pack("US", new() { ["Sd.Customer.State"] = new("REQUIRED", null, null) }, new() { ["Sd.Customer.Country"] = "US" });

    private static readonly CountryPack Kr = Pack(
        "KR",
        new()
        {
            ["Sd.Customer.State"] = new("HIDDEN", null, null),
            ["Sd.Customer.BusinessRegNo"] = new("OPTIONAL", "x", "Kr.Label.BusinessRegNo"),
            ["Sd.Customer.ext.Messenger"] = new("OPTIONAL", null, null),
        },
        new() { ["Sd.Customer.Country"] = "KR", ["Sd.Customer.BusinessRegNo"] = "000" });

    [Fact, Trait("Id", "BC-UT-430"), Trait("Rule", "BC-BR-430")]
    public void Merge_TheCompanyCountryPackDecides_TheFieldSet()
    {
        var us = FieldPolicy.Merge(Screen, Us, NoOverrides);
        var kr = FieldPolicy.Merge(Screen, Kr, NoOverrides);

        (us.Get("Sd.Customer.State").State, kr.Get("Sd.Customer.State").State).Should().Be(("REQUIRED", "HIDDEN"));
        (us.Get("Sd.Customer.Country").DefaultValue, kr.Get("Sd.Customer.Country").DefaultValue).Should().Be(("US", "KR"));
    }

    [Fact, Trait("Id", "BC-UT-431"), Trait("Rule", "BC-BR-431")]
    public void Merge_WithoutAPack_EveryCatalogFieldIsOptional_WithItsKeyAsTheLabel()
    {
        var policy = FieldPolicy.Merge(Screen, null, NoOverrides);

        policy.Fields.Should().HaveCount(FieldCatalog.FieldsOf(Screen)!.Count)
            .And.OnlyContain(f => f.State == FieldState.Optional && f.LabelKey == f.FieldKey && f.Source == FieldPolicySource.System);
        policy.Fields[0].FieldKey.Should().Be("Sd.Customer.CName", "catalog order");
        policy.Get("Sd.Customer.Nothing").State.Should().Be(FieldState.Optional);
    }

    [Fact, Trait("Id", "BC-UT-431"), Trait("Rule", "BC-BR-431")]
    public void Merge_ALaterLayerCoversAnEarlierOne_PartByPart()
    {
        var company = new Dictionary<string, FieldPolicyEntry>
        {
            ["Sd.Customer.State"] = new("READONLY", null, null),
            ["Sd.Customer.BusinessRegNo"] = new(null, null, "Company.Label.TaxId"),
            ["Mm.Vendor.State"] = new("REQUIRED", null, null),
        };

        var policy = FieldPolicy.Merge(Screen, Kr, company);

        policy.Get("Sd.Customer.State").Should().Be(new ResolvedField("Sd.Customer.State", "READONLY", null, "Sd.Customer.State", FieldPolicySource.Company));
        policy.Get("Sd.Customer.BusinessRegNo").Should().Be(
            new ResolvedField("Sd.Customer.BusinessRegNo", "OPTIONAL", "000", "Company.Label.TaxId", FieldPolicySource.Company),
            "the company label covers the pack label; the state and the defaults.yaml value stay");
        policy.Get("Sd.Customer.Country").Source.Should().Be(FieldPolicySource.CountryPack);
        policy.Fields.Should().NotContain(f => f.FieldKey.StartsWith("Mm.", StringComparison.Ordinal), "another screen's override");
        policy.ExtFields.Should().ContainSingle().Which.FieldKey.Should().Be("Sd.Customer.ext.Messenger");
    }

    [Fact, Trait("Id", "BC-UT-433"), Trait("Rule", "BC-BR-433")]
    public void NormalizeOverride_TrimsAndUpperCases_AndBlankPartsKeepThePack()
    {
        var (key, entry) = FieldPolicyRules.NormalizeOverride(" Sd.Customer.State ", " hidden ", " ", null, Kr);

        (key, entry).Should().Be(("Sd.Customer.State", new FieldPolicyEntry("HIDDEN", null, null)));
    }

    [Theory, Trait("Id", "BC-UT-433"), Trait("Rule", "BC-BR-433")]
    [InlineData("Sd.Customer.CName", "HIDDEN", null)]
    [InlineData("Sd.Customer.ZipCode", "HIDDEN", null)]
    [InlineData("Mm.Vendor.VName", "HIDDEN", null)]
    [InlineData("Sd.Customer.State", "SHOWN", null)]
    [InlineData("Sd.Customer.Unknown", "OPTIONAL", null)]
    [InlineData("Sd.Customer.ext.Undeclared", "OPTIONAL", null)]
    [InlineData("Sd.Customer.Country", null, "JP")]
    [InlineData("", "OPTIONAL", null)]
    public void NormalizeOverride_NotAllowed_IsBcE433WithTheKey(string key, string? state, string? defaultValue)
    {
        var act = () => FieldPolicyRules.NormalizeOverride(key, state, defaultValue, null, Kr);

        var error = act.Should().Throw<DomainException>().Which;
        (error.Code, error.Args[0]).Should().Be(("BC-E-433", key));
    }

    [Fact, Trait("Id", "BC-UT-433"), Trait("Rule", "BC-BR-433")]
    public void NormalizeOverride_ADeclaredExtKeyAndAHiddenOptionalField_AreAllowed()
    {
        FieldPolicyRules.NormalizeOverride("Sd.Customer.ext.Messenger", "REQUIRED", null, null, Kr).FieldKey.Should().Be("Sd.Customer.ext.Messenger");
        FieldPolicyRules.NormalizeOverride("Sd.Customer.Email", "HIDDEN", null, null, Kr).Entry.State.Should().Be("HIDDEN");
        FieldPolicyRules.NormalizeOverride("Sd.Customer.ShipCountry", null, "US", null, Kr).Entry.DefaultValue.Should().Be("US");
    }

    [Fact, Trait("Id", "BC-UT-437"), Trait("Rule", "BC-BR-437")]
    public void CheckPack_FindsEveryKindOfProblem()
    {
        var pack = new CountryPack(
            "XX",
            new Dictionary<string, FieldPolicyEntry>
            {
                ["Sd.Customer.CName"] = new("HIDDEN", null, null),
                ["Sd.Customer.State"] = new("MAYBE", null, null),
                ["Sd.Customer.Typo"] = new("OPTIONAL", null, null),
                ["Sd.Customer.CeoName"] = new("OPTIONAL", null, "Xx.Label.Ceo"),
            },
            new Dictionary<string, string> { ["Sd.Customer.Country"] = "XX", ["Sd.Customer.ext.Messenger"] = "none" },
            new Dictionary<string, IReadOnlyDictionary<string, string>> { ["en"] = new Dictionary<string, string>(), ["ko"] = new Dictionary<string, string> { ["Xx.Label.Ceo"] = "대표" } });

        FieldPolicyRules.CheckPack(pack).Should().BeEquivalentTo(
            "XX field-policy.yaml Sd.Customer.Typo: not a field of a policy screen",
            "XX defaults.yaml Sd.Customer.ext.Messenger: not a field of a policy screen or a declared ext. key",
            "XX Sd.Customer.CName: a required business field cannot be HIDDEN",
            "XX Sd.Customer.State: state MAYBE is not one of HIDDEN, OPTIONAL, REQUIRED, READONLY",
            "XX Sd.Customer.Country: default XX is not a supported country",
            "XX labels.en.yaml: no text for Xx.Label.Ceo");
    }

    [Fact, Trait("Id", "BC-UT-437"), Trait("Rule", "BC-BR-437")]
    public void CheckPack_AValidPack_HasNoProblems()
    {
        FieldPolicyRules.CheckPack(Kr with { Labels = new Dictionary<string, IReadOnlyDictionary<string, string>>() }).Should().BeEmpty();
    }

    [Theory, Trait("Id", "BC-UT-432"), Trait("Rule", "BC-BR-432")]
    [InlineData(null, true)]
    [InlineData(" ", true)]
    [InlineData("A", false)]
    [InlineData(0, false)]
    public void IsEmpty_NullOrBlank(object? value, bool empty)
    {
        FieldPolicyRules.IsEmpty(value).Should().Be(empty);
        FieldPolicyRules.IsEmpty(Array.Empty<int>()).Should().BeTrue();
    }

    private static CountryPack Pack(string country, Dictionary<string, FieldPolicyEntry> policies, Dictionary<string, string> defaults) =>
        new(country, policies, defaults, new Dictionary<string, IReadOnlyDictionary<string, string>>());
}
