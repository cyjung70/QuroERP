using System.Data;
using System.Text;
using FluentAssertions;
using QuroErp.Application.Mm.Labels;
using QuroErp.Application.Tests.Common.Auth;
using QuroErp.Domain.Mm.Labels;

namespace QuroErp.Application.Tests.Mm.Labels;

/// <summary>Label output (ZPL, PDF), page order and print history (MM-BR-917~919).</summary>
public class LabelPrintServiceTests
{
    private static readonly LabelFormat ItemZpl = new(LabelType.Item, LabelType.Zpl, 50, 25, 203, ["itemCd", "itemName"]);
    private static readonly LabelFormat PalletZpl = new(LabelType.Pallet, LabelType.Zpl, 100, 150, 203, ["palletNo", "qty"]);

    private readonly FakePdf _pdf = new();
    private readonly FakeHistory _history = new();
    private readonly FakeUnitOfWorkFactory _unitOfWork = new();

    private LabelPrintService Service => new(_pdf, _history, _unitOfWork);

    [Fact, Trait("Id", "MM-UT-919"), Trait("Rule", "MM-BR-919")]
    public async Task PrintAsync_Zpl_MatchesTheSample_EscapesData_AndKeepsTheHistory()
    {
        var label = new Label(LabelType.Item, 31, new Dictionary<string, string?> { ["itemCd"] = "A_1^", ["itemName"] = "볼트" }, "IT|A_1^");

        var output = await Service.PrintAsync(ItemZpl, [label], 2, "ZEBRA-1", "kim", CancellationToken.None);

        output.ContentType.Should().Be(LabelPrintService.ZplContentType);
        Encoding.UTF8.GetString(output.Content).Should().Be(
            "^XA^CI28^PW400^LL200"
            + "^FO24,24^A0N,28,28^FH_^FDA_5F1_5E^FS"
            + "^FO24,60^A0N,28,28^FH_^FD볼트^FS"
            + "^FO24,96^BXN,4,200^FH_^FDIT|A_5F1_5E^FS"
            + "^PQ2^XZ\n");
        _history.Rows.Should().Equal(new LabelPrintRow(LabelType.Item, 31, 2, LabelType.Zpl, "ZEBRA-1", "kim"));
        _unitOfWork.Commits.Should().Be(1);
        _pdf.Calls.Should().Be(0);
    }

    [Fact, Trait("Id", "MM-UT-918"), Trait("Rule", "MM-BR-918")]
    public async Task PrintAsync_APallet_PrintsItsLabelThenABarcodeOnlyLabel()
    {
        var pallet = new Label(LabelType.Pallet, 5, new Dictionary<string, string?> { ["palletNo"] = "PL2610000001", ["qty"] = "24" },
            "PL|PL2610000001|A|B1|24|");

        var zpl = Encoding.UTF8.GetString((await Service.PrintAsync(PalletZpl, [pallet], 1, null, "kim", CancellationToken.None)).Content);

        zpl.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().Equal(
            "^XA^CI28^PW799^LL1199^FO24,24^A0N,28,28^FH_^FDPL2610000001^FS^FO24,60^A0N,28,28^FH_^FD24^FS^PQ1^XZ",
            "^XA^CI28^PW799^LL1199^FO24,24^BXN,8,200^FH_^FDPL|PL2610000001|A|B1|24|^FS^PQ1^XZ");
        _history.Rows.Should().ContainSingle().Which.RefId.Should().Be(5);
    }

    [Fact, Trait("Id", "MM-UT-919"), Trait("Rule", "MM-BR-919")]
    public async Task PrintAsync_Pdf_RendersThePagesInOrder_AndKeepsOneRowPerLabel()
    {
        var format = PalletZpl with { OutputKind = LabelType.Pdf };
        Label[] pallets =
        [
            new(LabelType.Pallet, 5, new Dictionary<string, string?>(), "PL|1"),
            new(LabelType.Pallet, 6, new Dictionary<string, string?>(), "PL|2"),
        ];

        var output = await Service.PrintAsync(format, pallets, 3, null, "kim", CancellationToken.None);

        output.Should().BeEquivalentTo(new LabelOutput(LabelType.Pdf, LabelPrintService.PdfContentType, [1, 2, 3]));
        _pdf.Pages.Select(p => (p.Label.RefId, p.Barcode)).Should().Equal((5L, false), (5L, true), (6L, false), (6L, true));
        _pdf.Copies.Should().Be(3);
        _history.Rows.Select(r => (r.RefId, r.Copies, r.OutputKind)).Should().Equal((5L, 3, "PDF"), (6L, 3, "PDF"));
    }

    [Fact, Trait("Id", "MM-UT-917"), Trait("Rule", "MM-BR-917")]
    public async Task PrintAsync_ALabelOfAnotherType_OrNoCopies_IsRefused()
    {
        var lot = new Label(LabelType.Lot, 1, new Dictionary<string, string?>(), "LT|A|B");

        var wrongType = () => Service.PrintAsync(ItemZpl, [lot], 1, null, "kim", CancellationToken.None);
        var noCopies = () => Service.PrintAsync(ItemZpl, [], 0, null, "kim", CancellationToken.None);

        await wrongType.Should().ThrowAsync<ArgumentException>();
        await noCopies.Should().ThrowAsync<ArgumentOutOfRangeException>();
        _history.Rows.Should().BeEmpty();
    }

    private sealed class FakePdf : ILabelPdfRenderer
    {
        public int Calls { get; private set; }

        public IReadOnlyList<LabelPage> Pages { get; private set; } = [];

        public int Copies { get; private set; }

        public Task<byte[]> RenderAsync(LabelFormat format, IReadOnlyList<LabelPage> pages, int copies, CancellationToken cancellationToken)
        {
            Calls++;
            Pages = pages;
            Copies = copies;
            return Task.FromResult<byte[]>([1, 2, 3]);
        }
    }

    private sealed class FakeHistory : ILabelPrintRepository
    {
        public List<LabelPrintRow> Rows { get; } = [];

        public Task AddAsync(IReadOnlyList<LabelPrintRow> rows, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Rows.AddRange(rows);
            return Task.CompletedTask;
        }
    }
}
