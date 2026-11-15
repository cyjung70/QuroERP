using System.Data;
using System.Text;
using FluentAssertions;
using QuroErp.Application.Common;
using QuroErp.Application.Mm.Labels;
using QuroErp.Application.Tests.Common;
using QuroErp.Application.Tests.Common.Auth;
using QuroErp.Contracts.Mm.Labels;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Labels;

namespace QuroErp.Application.Tests.Mm.Labels;

/// <summary>Barcode label printing (MM-UC-037): targets, a print with copies per target, and the history (MM-BR-918, MM-BR-919).</summary>
public class LabelPrintRequestServiceTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly FakeTargets _targets = new();
    private readonly FakeFormats _formats = new();
    private readonly FakeHistory _history = new();
    private readonly FakePdf _pdf = new();
    private readonly FakeUnitOfWorkFactory _unitOfWork = new();
    private readonly FakeBusinessClock _clock = new() { TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul") };

    private LabelPrintRequestService Service => new(
        _targets, _formats, _history, new LabelPrintService(_pdf, _history, _unitOfWork), _clock, _unitOfWork);

    private static LabelPrintRequest Request(string labelType, params (long Id, int Copies)[] targets) =>
        new(labelType, [.. targets.Select(t => new LabelPrintTarget(t.Id, t.Copies))], null, "ZEBRA-1");

    [Fact, Trait("Id", "MM-UC-037-T01"), Trait("Id", "MM-UC-037-T03"), Trait("Rule", "MM-BR-918")]
    public async Task Print_PalletPdf_EachCopyIsTheLabelThenItsDataMatrix_InTheOrderGiven()
    {
        _formats.OutputKind = LabelType.Pdf;

        var output = await Service.PrintAsync(Request("pallet", (2, 1), (1, 2)), "kim", None);

        output.ContentType.Should().Be(LabelPrintService.PdfContentType);
        _pdf.Copies.Should().Be(1);
        _pdf.Pages.Select(p => (p.Label.RefId, p.Text, p.Barcode)).Should().Equal(
            (2L, true, false), (2L, false, true),
            (1L, true, false), (1L, false, true),
            (1L, true, false), (1L, false, true));
        _pdf.Pages[0].Label.Code.Should().Be("PL|PL2611000002|BOLT|L1|4|20270331");
    }

    [Fact, Trait("Id", "MM-UC-037-T03"), Trait("Id", "MM-UC-037-T04"), Trait("Id", "MM-UT-919"), Trait("Rule", "MM-BR-919")]
    public async Task Print_ItemZpl_GivesTheText_AndKeepsOneHistoryRowPerTargetWithItsCopies()
    {
        var output = await Service.PrintAsync(Request("ITEM", (10, 2), (11, 1)), "kim", None);

        output.OutputKind.Should().Be(LabelType.Zpl);
        var zpl = Encoding.UTF8.GetString(output.Content);
        zpl.Split("^XZ\n", StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(3, "two copies of BOLT and one of NUT");
        zpl.Should().Contain("^FDIT|BOLT^FS").And.Contain("^FDIT|NUT^FS").And.Contain("^PQ1^XZ");
        _history.Rows.Should().Equal(
            new LabelPrintRow(LabelType.Item, 10, 2, LabelType.Zpl, "ZEBRA-1", "kim"),
            new LabelPrintRow(LabelType.Item, 11, 1, LabelType.Zpl, "ZEBRA-1", "kim"));
        _unitOfWork.Commits.Should().Be(2, "one read and one history write");
    }

    [Fact, Trait("Rule", "MM-BR-919")]
    public async Task Print_OutputKindOfTheRequest_ReplacesTheFormats()
    {
        var output = await Service.PrintAsync(Request("LOCATION", (30, 1)) with { OutputKind = "pdf" }, "kim", None);

        output.OutputKind.Should().Be(LabelType.Pdf);
        _pdf.Pages.Should().ContainSingle().Which.Label.Code.Should().Be("LC|MAIN|A-01");
        _history.Rows.Should().ContainSingle().Which.OutputKind.Should().Be(LabelType.Pdf);
    }

    public static TheoryData<LabelPrintRequest, string, string> Invalid() => new()
    {
        { new LabelPrintRequest(null, [new(10, 1)], null, null), "Common-E-005", "labelType" },
        { new LabelPrintRequest("BOX", [new(10, 1)], null, null), "Common-E-002", "labelType" },
        { new LabelPrintRequest("ITEM", [], null, null), "Common-E-005", "targets" },
        { new LabelPrintRequest("ITEM", [new(10, 0)], null, null), "Common-E-005", "copies" },
        { new LabelPrintRequest("ITEM", [new(10, LabelPrintRequestService.MaxCopies + 1)], null, null), "Common-E-005", "copies" },
        { new LabelPrintRequest("ITEM", [new(10, 1)], "PNG", null), "Common-E-002", "outputKind" },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public async Task Print_WithAMissingOrUnknownValue_Throws422_AndPrintsNothing(LabelPrintRequest request, string code, string field)
    {
        var act = () => Service.PrintAsync(request, "kim", None);

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        (error.Code, error.Args[0]).Should().Be((code, (object)field));
        _history.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Print_AMissingTargetOrAFormatNotInUse_Throws404()
    {
        var missing = () => Service.PrintAsync(Request("ITEM", (10, 1), (99, 1)), "kim", None);
        await missing.Should().ThrowAsync<NotFoundException>().WithMessage("Common-E-006: 99");

        _formats.InUse = false;
        var notInUse = () => Service.PrintAsync(Request("ITEM", (10, 1)), "kim", None);
        await notInUse.Should().ThrowAsync<NotFoundException>().WithMessage("Common-E-006: ITEM");
        _history.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Targets_PassTheConditions_AndALocationListNeedsAWarehouse()
    {
        var pallets = await Service.TargetsAsync(new LabelTargetQuery { LabelType = "pallet", GoodsReceiptId = 5, ItemId = 10 }, None);
        var noWarehouse = () => Service.TargetsAsync(new LabelTargetQuery { LabelType = "LOCATION" }, None);

        pallets.Select(p => p.Id).Should().Equal(1, 2);
        _targets.Filters[^1].Should().Be((LabelType.Pallet, new LabelTargetFilter(5, 10, null, null, LabelPrintRoutes.MaxListSize)));
        (await noWarehouse.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal("warehouseId");
    }

    [Fact, Trait("Id", "MM-UC-037-T04"), Trait("Rule", "MM-BR-919")]
    public async Task History_ReadsTheCompanyDates_FromMidnightToTheNextMidnight()
    {
        await Service.HistoryAsync(new LabelPrintHistoryQuery { From = new(2026, 11, 1), To = new(2026, 11, 2), LabelType = "pallet" }, None);

        // Seoul is UTC+9
        _history.Reads.Should().Equal((
            new DateTimeOffset(2026, 10, 31, 15, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 2, 15, 0, 0, TimeSpan.Zero),
            LabelType.Pallet));
    }

    private sealed class FakeTargets : ILabelTargetRepository
    {
        private static readonly DateOnly Expiry = new(2027, 3, 31);

        private static readonly LabelTargetDto[] All =
        [
            new(1, LabelType.Pallet, "PL2611000001", "Hex Bolt", "BOLT", "L1", 6, Expiry, "MAIN", "A-01"),
            new(2, LabelType.Pallet, "PL2611000002", "Hex Bolt", "BOLT", "L1", 4, Expiry, "MAIN", "A-01"),
            new(10, LabelType.Item, "BOLT", "Hex Bolt", "BOLT", null, null, null, null, null),
            new(11, LabelType.Item, "NUT", "Nut", "NUT", null, null, null, null, null),
            new(30, LabelType.Location, "A-01", "Shelf A1", null, null, null, null, "MAIN", "A-01"),
        ];

        public List<(string LabelType, LabelTargetFilter Filter)> Filters { get; } = [];

        public Task<IReadOnlyList<LabelTargetDto>> FindAsync(
            string labelType, LabelTargetFilter filter, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Filters.Add((labelType, filter));
            return Task.FromResult<IReadOnlyList<LabelTargetDto>>(
                [.. All.Where(t => t.LabelType == labelType && (filter.Ids is null || filter.Ids.Contains(t.Id)))]);
        }
    }

    private sealed class FakeFormats : ILabelFormatRepository
    {
        public string OutputKind { get; set; } = LabelType.Zpl;

        public bool InUse { get; set; } = true;

        public Task<LabelFormat?> FindAsync(string labelType, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(InUse ? new LabelFormat(labelType, OutputKind, 50, 25, 203, LabelFormatRules.FieldsOf(labelType)) : null);

        public Task<IReadOnlyList<LabelFormatRow>> ListAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<LabelFormatRow>> LockAsync(
            IReadOnlyCollection<string> labelTypes, IDbTransaction transaction, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpdateAsync(LabelFormat format, bool useYn, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeHistory : ILabelPrintRepository, ILabelPrintHistoryRepository
    {
        public List<LabelPrintRow> Rows { get; } = [];

        public List<(DateTimeOffset? From, DateTimeOffset? Before, string? LabelType)> Reads { get; } = [];

        public Task AddAsync(IReadOnlyList<LabelPrintRow> rows, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Rows.AddRange(rows);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LabelPrintHistoryDto>> FindAsync(
            DateTimeOffset? printedFrom, DateTimeOffset? printedBefore, string? labelType, int limit, IDbTransaction transaction,
            CancellationToken cancellationToken)
        {
            Reads.Add((printedFrom, printedBefore, labelType));
            return Task.FromResult<IReadOnlyList<LabelPrintHistoryDto>>([]);
        }
    }

    private sealed class FakePdf : ILabelPdfRenderer
    {
        public IReadOnlyList<LabelPage> Pages { get; private set; } = [];

        public int Copies { get; private set; }

        public Task<byte[]> RenderAsync(LabelFormat format, IReadOnlyList<LabelPage> pages, int copies, CancellationToken cancellationToken)
        {
            (Pages, Copies) = (pages, copies);
            return Task.FromResult("%PDF"u8.ToArray());
        }
    }
}
