using Dapper;
using FluentAssertions;
using Npgsql;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Mm.Labels;
using QuroErp.Contracts.Common;
using QuroErp.Contracts.Mm.Labels;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Labels;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Mm.Labels;

/// <summary>Own schema for label format tests: the saves commit, so their rows must not reach other test classes.</summary>
[CollectionDefinition(Name)]
public sealed class LabelFormatSchemaDefinition : ICollectionFixture<AuthSchemaFixture>
{
    public const string Name = "LabelFormatSchema";
}

/// <summary>LabelFormatService with the Dapper repository on real PostgreSQL (MM-UC-036, MM-BR-917).</summary>
[Collection(LabelFormatSchemaDefinition.Name)]
public class LabelFormatServiceDbTests(AuthSchemaFixture db)
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly string[] LotFields = ["lotNo", "itemCd"];

    private readonly FakePdf _pdf = new();

    private LabelFormatService Service => new(db.Get<ILabelFormatRepository>(), _pdf, db.Get<IUnitOfWorkFactory>());

    private static BatchRequest<LabelFormatSaveRow, LabelFormatKey> Batch(params LabelFormatSaveRow[] updated) => new([], updated, []);

    [DatabaseFact, Trait("Id", "MM-UC-036-T01"), Trait("Rule", "MM-BR-917")]
    public async Task Install_MakesTheFourDefaultFormats()
    {
        var list = await Service.ListAsync(None);

        list.Select(f => (f.LabelType, f.OutputKind, f.WidthMm, f.HeightMm, f.Dpi, f.UseYn)).Should().Equal(
            ("PALLET", "ZPL", 100m, 150m, 203, true),
            ("ITEM", "ZPL", 50m, 25m, 203, true),
            ("LOT", "ZPL", 50m, 25m, 203, true),
            ("LOCATION", "ZPL", 100m, 50m, 203, true));
        list[0].Fields.Should().Equal("palletNo", "itemCd", "itemName", "lotNo", "qty", "expiryDt");
    }

    [DatabaseFact, Trait("Id", "MM-UT-917"), Trait("Rule", "MM-BR-917")]
    public async Task Save_StoresTheFormat_ThePrintFormatFollowsIt_AndAStaleRowSavesNothing()
    {
        var lot = (await Service.ListAsync(None)).Single(f => f.LabelType == LabelType.Lot);

        var after = await Service.SaveAsync(Batch(new LabelFormatSaveRow("LOT", "pdf", 60.5m, 30, 300, LotFields, false, lot.UpdatedAt)), "kim", None);
        var stale = () => Service.SaveAsync(Batch(new LabelFormatSaveRow("LOT", "ZPL", 70, 30, 203, LotFields, true, lot.UpdatedAt)), "kim", None);

        after.Single(f => f.LabelType == "LOT").Should().BeEquivalentTo(
            new { OutputKind = "PDF", WidthMm = 60.5m, HeightMm = 30m, Dpi = 300, Fields = LotFields, UseYn = false });
        await stale.Should().ThrowAsync<ConcurrencyException>();
        (await FindForPrintAsync(LabelType.Lot)).Should().BeNull("a format not in use does not print");
        (await ScalarAsync<string>("SELECT updated_by FROM bc_label_format WHERE label_type = 'LOT'")).Should().Be("kim");

        // Back to the install format, so the default format test sees it in any order
        var changed = (await Service.ListAsync(None)).Single(f => f.LabelType == LabelType.Lot);
        await Service.SaveAsync(Batch(new LabelFormatSaveRow("LOT", "ZPL", 50, 25, 203, lot.Fields, true, changed.UpdatedAt)), "kim", None);
        (await FindForPrintAsync(LabelType.Lot))!.Fields.Should().Equal(lot.Fields);
    }

    [DatabaseFact, Trait("Id", "MM-UC-036-T02"), Trait("Rule", "MM-BR-917")]
    public async Task Save_WithASizeOf0_IsBlocked_AndTheDatabaseKeepsTheFormat()
    {
        var item = (await Service.ListAsync(None)).Single(f => f.LabelType == LabelType.Item);

        var act = () => Service.SaveAsync(Batch(new LabelFormatSaveRow("ITEM", "ZPL", 0, 25, 203, ["itemCd"], true, item.UpdatedAt)), "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("MM-E-917");
        (await ScalarAsync<decimal>("SELECT width_mm FROM bc_label_format WHERE label_type = 'ITEM'")).Should().Be(item.WidthMm);
    }

    [DatabaseFact, Trait("Id", "MM-UC-036-T03"), Trait("Rule", "MM-BR-918")]
    public async Task Preview_RendersTheSampleInTheStoredLocationFormat()
    {
        var pdf = await Service.PreviewAsync("location", None);

        pdf.Should().Equal(FakePdf.Bytes);
        _pdf.Format.Should().BeEquivalentTo(new { LabelType = "LOCATION", WidthMm = 100m, HeightMm = 50m });
        _pdf.Pages.Should().ContainSingle().Which.Label.Code.Should().Be("LC|MAIN|A-01-01");
    }

    private async Task<LabelFormat?> FindForPrintAsync(string labelType)
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var format = await db.Get<ILabelFormatRepository>().FindAsync(labelType, uow.Transaction, None);
        await uow.CommitAsync(None);
        return format;
    }

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        return (await connection.ExecuteScalarAsync<T>(sql))!;
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
