using System.Data;
using FluentAssertions;
using QuroErp.Application.Common;
using QuroErp.Application.Mm.Labels;
using QuroErp.Application.Tests.Common.Auth;
using QuroErp.Contracts.Common;
using QuroErp.Contracts.Mm.Labels;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Labels;

namespace QuroErp.Application.Tests.Mm.Labels;

/// <summary>Label format list, batch save and sample PDF (MM-UC-036, MM-BR-917, MM-BR-918).</summary>
public class LabelFormatServiceTests
{
    private static readonly DateTimeOffset Stored = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly string[] ItemFields = ["itemName", "itemCd"];

    private readonly FakeFormats _formats = new();
    private readonly FakePdf _pdf = new();
    private readonly FakeUnitOfWorkFactory _unitOfWork = new();

    private LabelFormatService Service => new(_formats, _pdf, _unitOfWork);

    private static BatchRequest<LabelFormatSaveRow, LabelFormatKey> Batch(
        LabelFormatSaveRow[]? updated = null, LabelFormatSaveRow[]? added = null, LabelFormatKey[]? deleted = null) =>
        new(added ?? [], updated ?? [], deleted ?? []);

    private static LabelFormatSaveRow ItemRow(decimal width = 60, DateTimeOffset? updatedAt = null, string labelType = "item") =>
        new(labelType, "pdf", width, 30, 300, ItemFields, false, updatedAt ?? Stored);

    [Fact, Trait("Id", "MM-UC-036-T01")]
    public async Task List_ReturnsTheFourFormats_WithTheFieldsEachTypeCanPrint()
    {
        var list = await Service.ListAsync(CancellationToken.None);

        list.Select(f => f.LabelType).Should().Equal(LabelFormatRules.Types);
        list[1].AvailableFields.Should().Equal(LabelField.ItemCd, LabelField.ItemName);
    }

    [Fact, Trait("Id", "MM-UT-917"), Trait("Rule", "MM-BR-917")]
    public async Task Save_UpdatesTheChangedRow_AndReturnsTheFourFormats()
    {
        var after = await Service.SaveAsync(Batch([ItemRow()]), "kim", CancellationToken.None);

        var (format, useYn, updatedBy) = _formats.Updates.Should().ContainSingle().Subject;
        format.Should().BeEquivalentTo(new LabelFormat(LabelType.Item, LabelType.Pdf, 60, 30, 300, ItemFields));
        (useYn, updatedBy).Should().Be((false, "kim"));
        after.Should().HaveCount(4);
        _unitOfWork.Commits.Should().Be(1);
    }

    [Fact, Trait("Id", "MM-UC-036-T02"), Trait("Rule", "MM-BR-917")]
    public async Task Save_WithASizeOf0_InAnyRow_SavesNothing()
    {
        var palletRow = new LabelFormatSaveRow("PALLET", "ZPL", 100, 150, 203, ["palletNo"], true, Stored);

        var act = () => Service.SaveAsync(Batch([palletRow, ItemRow(width: 0)]), "kim", CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Should().BeEquivalentTo(new { Code = "MM-E-917", Args = new object[] { "ITEM" } });
        _formats.Updates.Should().BeEmpty();
    }

    public static TheoryData<BatchRequest<LabelFormatSaveRow, LabelFormatKey>> NotSavable() =>
    [
        Batch(added: [ItemRow()]),
        Batch(deleted: [new LabelFormatKey("ITEM", Stored)]),
        Batch([ItemRow(labelType: "BOX")]),
        Batch([ItemRow(), ItemRow()]),
        Batch([new LabelFormatSaveRow("ITEM", null, 50, 25, 203, ItemFields, true, Stored)]),
    ];

    [Theory, Trait("Id", "MM-UT-917"), Trait("Rule", "MM-BR-917")]
    [MemberData(nameof(NotSavable))]
    public async Task Save_AddingDeletingAnUnknownOrRepeatedTypeOrAMissingValue_Throws917(
        BatchRequest<LabelFormatSaveRow, LabelFormatKey> request)
    {
        var act = () => Service.SaveAsync(request, "kim", CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(LabelFormatRules.InvalidCode);
        _formats.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_AStaleRow_Throws409_AndSavesNothing()
    {
        var act = () => Service.SaveAsync(Batch([ItemRow(updatedAt: Stored.AddMinutes(-1))]), "kim", CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyException>();
        _formats.Updates.Should().BeEmpty();
    }

    [Fact, Trait("Id", "MM-UC-036-T03"), Trait("Rule", "MM-BR-918")]
    public async Task Preview_RendersTheSampleInTheStoredFormat_EvenWhenZpl_AndThePalletTakesTwoPages()
    {
        var pdf = await Service.PreviewAsync("pallet", CancellationToken.None);

        pdf.Should().Equal(FakePdf.Bytes);
        _pdf.Format!.Should().Be(_formats.Rows[0].Format);
        _pdf.Pages.Select(p => (p.Label.Code, p.Text, p.Barcode)).Should().Equal(
            ("PL|PL2611000001|ITEM-001|261101-001|10|20271231", true, false),
            ("PL|PL2611000001|ITEM-001|261101-001|10|20271231", false, true));
    }

    [Fact]
    public async Task Preview_OfAnUnknownType_Throws404()
    {
        var act = () => Service.PreviewAsync("BOX", CancellationToken.None);

        (await act.Should().ThrowAsync<NotFoundException>()).Which.Message.Should().StartWith("Common-E-006");
    }

    private sealed class FakeFormats : ILabelFormatRepository
    {
        public List<LabelFormatRow> Rows { get; } =
        [
            new(new(LabelType.Pallet, LabelType.Zpl, 100, 150, 203, ["palletNo", "qty"]), true, Stored),
            new(new(LabelType.Item, LabelType.Zpl, 50, 25, 203, ["itemCd", "itemName"]), true, Stored),
            new(new(LabelType.Lot, LabelType.Zpl, 50, 25, 203, ["itemCd", "lotNo"]), true, Stored),
            new(new(LabelType.Location, LabelType.Zpl, 100, 50, 203, ["warehouseCode", "locationCode"]), true, Stored),
        ];

        public List<(LabelFormat Format, bool UseYn, string UpdatedBy)> Updates { get; } = [];

        public Task<LabelFormat?> FindAsync(string labelType, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(Rows.FirstOrDefault(r => r.Format.LabelType == labelType && r.UseYn)?.Format);

        public Task<IReadOnlyList<LabelFormatRow>> ListAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LabelFormatRow>>(Rows);

        public Task<IReadOnlyList<LabelFormatRow>> LockAsync(
            IReadOnlyCollection<string> labelTypes, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LabelFormatRow>>([.. Rows.Where(r => labelTypes.Contains(r.Format.LabelType))]);

        public Task UpdateAsync(LabelFormat format, bool useYn, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Updates.Add((format, useYn, updatedBy));
            return Task.CompletedTask;
        }
    }

    private sealed class FakePdf : ILabelPdfRenderer
    {
        public static readonly byte[] Bytes = "%PDF"u8.ToArray();

        public LabelFormat? Format { get; private set; }

        public IReadOnlyList<LabelPage> Pages { get; private set; } = [];

        public Task<byte[]> RenderAsync(LabelFormat format, IReadOnlyList<LabelPage> pages, int copies, CancellationToken cancellationToken)
        {
            (Format, Pages) = (format, pages);
            return Task.FromResult(Bytes);
        }
    }
}
