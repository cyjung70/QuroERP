using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Contracts.Mm.Labels;

namespace QuroErp.Api.Tests.Mm.Labels;

/// <summary>Barcode label print API (MM-UC-037, TASK-MM-057) on the in-memory targets of <see cref="InMemoryLabelTargetRepository"/>.</summary>
public class LabelPrintApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static LabelPrintRequest Request(string labelType, string? outputKind, params (long Id, int Copies)[] targets) =>
        new(labelType, [.. targets.Select(t => new LabelPrintTarget(t.Id, t.Copies))], outputKind, "ZEBRA-1");

    [Fact]
    public async Task Targets_ForAnySignedInCaller_FollowTheConditions_AndNeedSignIn()
    {
        var client = factory.NonAdmin();

        var pallets = await client.GetFromJsonAsync<List<LabelTargetDto>>($"{LabelPrintRoutes.Targets}?labelType=pallet&goodsReceiptId=5");
        var none = await client.GetFromJsonAsync<List<LabelTargetDto>>($"{LabelPrintRoutes.Targets}?labelType=PALLET&warehouseId=2");
        using var anonymous = await factory.CreateClient().GetAsync($"{LabelPrintRoutes.Targets}?labelType=ITEM");

        pallets!.Select(p => (p.Code, p.Qty)).Should().Equal(("PL2611000001", 6m), ("PL2611000002", 4m));
        none.Should().BeEmpty();
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("labelType=LOCATION", "Common-E-005")]
    [InlineData("labelType=BOX", "Common-E-002")]
    [InlineData("", "Common-E-005")]
    public async Task Targets_WithoutTheirConditions_Return422(string query, string code)
    {
        using var response = await factory.NonAdmin().GetAsync($"{LabelPrintRoutes.Targets}?{query}");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be(code);
    }

    [Fact, Trait("Id", "MM-UC-037-T03"), Trait("Rule", "MM-BR-919")]
    public async Task Print_Zpl_ReturnsTheTextAsJson_AndPdf_ReturnsAPdf()
    {
        var client = await factory.AdminAsync();

        using var zpl = await client.PostAsJsonAsync(LabelPrintRoutes.Base, Request("LOCATION", null, (30, 1)));
        using var pdf = await client.PostAsJsonAsync(LabelPrintRoutes.Base, Request("PALLET", "PDF", (2, 1), (1, 1)));

        zpl.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = (await zpl.Content.ReadFromJsonAsync<LabelPrintZplDto>())!;
        text.OutputKind.Should().Be("ZPL");
        text.Zpl.Should().StartWith("^XA").And.Contain("^FDLC|MAIN|A-01^FS");
        pdf.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        (await pdf.Content.ReadAsStringAsync()).Should().Be("%PDF-fake pages=4", "each Pallet is its label and its DataMatrix");
    }

    [Fact, Trait("Id", "MM-UC-037-T04"), Trait("Rule", "MM-BR-919")]
    public async Task Print_KeepsTheHistory_ThatTheHistoryApiReads()
    {
        var client = await factory.AdminAsync();

        using var printed = await client.PostAsJsonAsync(LabelPrintRoutes.Base, Request("LOT", "PDF", (20, 3)));
        var history = await factory.NonAdmin().GetFromJsonAsync<List<LabelPrintHistoryDto>>($"{LabelPrintRoutes.History}?labelType=lot");

        printed.StatusCode.Should().Be(HttpStatusCode.OK);
        history.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { LabelType = "LOT", RefId = 20L, Copies = 3, OutputKind = "PDF", PrinterName = "ZEBRA-1", PrintedBy = ApiFactory.UserId });
    }

    [Fact]
    public async Task Print_AMissingTarget_Returns404_AndZeroCopies_Returns422()
    {
        var client = await factory.AdminAsync();

        using var missing = await client.PostAsJsonAsync(LabelPrintRoutes.Base, Request("ITEM", null, (99, 1)));
        using var zero = await client.PostAsJsonAsync(LabelPrintRoutes.Base, Request("ITEM", null, (10, 0)));

        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await missing.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("Common-E-006");
        zero.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await zero.ReadProblemAsync()).GetProperty("args")[0].GetString().Should().Be("copies");
    }

    [Fact, Trait("Id", "BC-UT-214")]
    public async Task Print_WithoutFullAccess_Returns403()
    {
        using var response = await factory.NonAdmin().PostAsJsonAsync(LabelPrintRoutes.Base, Request("ITEM", null, (10, 1)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
