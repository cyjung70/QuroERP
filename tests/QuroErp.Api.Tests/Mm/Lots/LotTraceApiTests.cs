using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using FluentAssertions;
using QuroErp.Contracts.Mm.Lots;

namespace QuroErp.Api.Tests.Mm.Lots;

/// <summary>
/// GET /api/mm/lot-trace (TASK-MM-063, MM-UC-040): sign-in, the required conditions, the tree rows and the XLSX of the same rows.
/// The trace itself is covered by LotTraceRulesTests and on real PostgreSQL by LotTraceServiceTests.
/// </summary>
public class LotTraceApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Forward = LotTraceRoutes.Base + "/" + LotTraceRoutes.Forward;
    private const string Backward = LotTraceRoutes.Base + "/" + LotTraceRoutes.Backward;

    [Fact, Trait("Id", "MM-UC-040-T01"), Trait("Rule", "MM-BR-366")]
    public async Task Forward_ReturnsTheTreeRows_ToAnySignedInUser()
    {
        var rows = await factory.NonAdmin().GetFromJsonAsync<List<LotTraceRowDto>>(
            $"{Forward}?itemId={InMemoryLotTraceRepository.ItemId}&lotNo={InMemoryLotTraceRepository.LotNo}");

        rows!.Select(r => (r.Key, r.ParentKey, r.DocType, r.PartyName, r.Qty)).Should().Equal(
            (1, (int?)null, "LOT", "Vendor A", (decimal?)null), (2, 1, "GR", "Vendor A", 10m), (3, 1, "SHIP", "Customer A", -3m));
    }

    [Fact, Trait("Id", "MM-UC-040-T02"), Trait("Rule", "MM-BR-367")]
    public async Task Backward_ByDocumentNumber_ReturnsTheIssueAndItsReceipt()
    {
        var rows = await factory.NonAdmin().GetFromJsonAsync<List<LotTraceRowDto>>($"{Backward}?docNo=sh-001");

        rows!.Select(r => (r.Level, r.DocType, r.DocNo)).Should().Equal((0, "SHIP", "SH-001"), (1, "GR", "GR-001"));
    }

    [Theory, Trait("Id", "MM-UT-366"), Trait("Rule", "MM-BR-363")]
    [InlineData("*")]
    [InlineData("OTHER")]
    public async Task Forward_OfLotStarOrAnUnknownLot_ReturnsNoRows(string lotNo)
    {
        var rows = await factory.NonAdmin().GetFromJsonAsync<List<LotTraceRowDto>>(
            $"{Forward}?itemId={InMemoryLotTraceRepository.ItemId}&lotNo={Uri.EscapeDataString(lotNo)}");

        rows.Should().BeEmpty();
    }

    [Theory, Trait("Id", "MM-UT-367")]
    [InlineData(Forward, "lotNo=A", "Common-E-005", "itemId")]
    [InlineData(Forward, "itemId=1", "Common-E-005", "lotNo")]
    [InlineData(Backward, "", "Common-E-005", "docNo")]
    [InlineData(Backward, "customerId=1&to=2026-11-30", "Common-E-005", "from")]
    [InlineData(Backward, "customerId=1&from=2026-11-01", "Common-E-005", "to")]
    [InlineData(Backward, "customerId=1&from=2026-11-30&to=2026-11-01", "Common-E-002", "to")]
    public async Task MissingOrWrongConditions_Return422(string path, string query, string code, string arg)
    {
        using var response = await factory.NonAdmin().GetAsync($"{path}?{query}");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be(code);
        problem.GetProperty("args")[0].GetString().Should().Be(arg);
    }

    [Fact, Trait("Id", "MM-UT-366")]
    public async Task WithoutSignIn_Returns401()
    {
        using var response = await factory.CreateClient().GetAsync($"{Forward}?itemId=1&lotNo=A");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, Trait("Id", "MM-UC-040-T04"), Trait("Rule", "MM-BR-369")]
    public async Task ExportXlsx_HasTheSameRowsAsTheScreen()
    {
        using var response = await factory.NonAdmin().GetAsync(
            $"{Forward}/{LotTraceRoutes.ExportXlsx}?itemId={InMemoryLotTraceRepository.ItemId}&lotNo={InMemoryLotTraceRepository.LotNo}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        response.Content.Headers.ContentDisposition!.FileNameStar.Should().Be("lot-trace-forward.xlsx");
        using var workbook = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        var sheet = workbook.Worksheets.Single();
        sheet.CellsUsed().Select(c => c.GetString()).Should().Contain(["GR-001-1", "SH-001-1", "Customer A"]);
    }
}
