using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Contracts.Mm.Labels;

namespace QuroErp.Api.Tests.Mm.Labels;

/// <summary>GET /api/mm/label-scans (TASK-CORE-071, MM-BR-918, MM-BR-923): the kind, its values and the item of a scan.</summary>
public class LabelScanApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory, Trait("Id", "MM-UT-923"), Trait("Rule", "MM-BR-923")]
    [InlineData("880000000017", "CABLE")]
    [InlineData("CABLE", "CABLE")]
    [InlineData(" BOLT ", "CABLE")]
    public async Task PlainValue_IsFoundAsABarcodeThenAnItemCode_AndComesBackAsAnItem(string value, string itemCd)
    {
        var scan = await factory.NonAdmin().GetFromJsonAsync<LabelScanDto>($"{LabelScanRoutes.Base}?value={Uri.EscapeDataString(value)}");

        scan!.Kind.Should().Be(LabelKinds.Item);
        scan.Parts.Should().Equal(itemCd);
        scan.ItemCd.Should().Be(itemCd);
        scan.ItemId.Should().Be(InMemoryLabelScanRepository.Cable.Id);
    }

    [Fact, Trait("Id", "MM-UT-923"), Trait("Rule", "MM-BR-918")]
    public async Task PalletLabel_ComesBackWithItsValuesAndItem()
    {
        var value = Uri.EscapeDataString("PL|PL2611000001|BOLT|261104-001|12|20270331");

        var scan = await factory.NonAdmin().GetFromJsonAsync<LabelScanDto>($"{LabelScanRoutes.Base}?value={value}&accept=PL&accept=LT");

        scan.Should().BeEquivalentTo(new LabelScanDto(
            "PL", ["PL2611000001", "BOLT", "261104-001", "12", "20270331"], InMemoryLabelScanRepository.Bolt.Id, "BOLT", "Hex Bolt"));
    }

    [Fact, Trait("Id", "MM-UT-923"), Trait("Rule", "MM-BR-918")]
    public async Task LocationLabel_HasNoItem()
    {
        var scan = await factory.NonAdmin().GetFromJsonAsync<LabelScanDto>($"{LabelScanRoutes.Base}?value={Uri.EscapeDataString("LC|MAIN|A01")}");

        scan.Should().BeEquivalentTo(new LabelScanDto("LC", ["MAIN", "A01"], null, null, null));
    }

    [Theory, Trait("Id", "MM-UT-923"), Trait("Rule", "MM-BR-923")]
    [InlineData("NOPE", "", "NOPE")]
    [InlineData("XX|A", "", "XX|A")]
    [InlineData("IT|NOPE", "", "IT|NOPE")]
    [InlineData("LC|MAIN|A01", "&accept=PL", "LC|MAIN|A01")]
    [InlineData("CABLE", "&accept=LC", "CABLE")]
    [InlineData("", "", "")]
    public async Task NotALabel_UnknownItem_OrAKindTheStepDoesNotTake_Returns422MmE923(string value, string accept, string arg)
    {
        using var response = await factory.NonAdmin().GetAsync($"{LabelScanRoutes.Base}?value={Uri.EscapeDataString(value)}{accept}");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be("MM-E-923");
        problem.GetProperty("args")[0].GetString().Should().Be(arg);
    }

    [Fact, Trait("Id", "MM-UT-923")]
    public async Task WithoutSignIn_Returns401()
    {
        using var response = await factory.CreateClient().GetAsync($"{LabelScanRoutes.Base}?value=CABLE");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
