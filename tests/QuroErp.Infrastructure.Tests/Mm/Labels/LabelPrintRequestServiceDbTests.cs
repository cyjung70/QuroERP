using System.Text;
using Dapper;
using FluentAssertions;
using Npgsql;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Mm.Labels;
using QuroErp.Contracts.Mm.Labels;
using QuroErp.Domain.Mm.Labels;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Mm.Labels;

/// <summary>Own schema for the label print tests: the prints commit their history, so its rows must not reach other test classes.</summary>
[CollectionDefinition(Name)]
public sealed class LabelPrintSchemaDefinition : ICollectionFixture<AuthSchemaFixture>
{
    public const string Name = "LabelPrintSchema";
}

/// <summary>
/// Label targets, a ZPL print and its history with the Dapper repositories on real PostgreSQL (MM-UC-037, TASK-MM-057). Each test
/// makes its own item, Lot, Location and Pallet on the default warehouse.
/// </summary>
[Collection(LabelPrintSchemaDefinition.Name)]
public class LabelPrintRequestServiceDbTests(AuthSchemaFixture db)
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly DateOnly Expiry = new(2027, 3, 31);

    private LabelPrintRequestService Service => new(
        db.Get<ILabelTargetRepository>(), db.Get<ILabelFormatRepository>(), db.Get<ILabelPrintHistoryRepository>(),
        new LabelPrintService(new NoPdf(), db.Get<ILabelPrintRepository>(), db.Get<IUnitOfWorkFactory>()), new UtcClock(),
        db.Get<IUnitOfWorkFactory>());

    [DatabaseFact, Trait("Id", "MM-UC-037-T01")]
    public async Task Targets_ReadEachType_WithItsValues_AndLeaveOutTheSystemLocationAndClosedPallets()
    {
        var m = await MastersAsync();

        var pallets = await Service.TargetsAsync(new() { LabelType = "PALLET", ItemId = m.Item }, None);
        var items = await Service.TargetsAsync(new() { LabelType = "ITEM", ItemId = m.Item }, None);
        var lots = await Service.TargetsAsync(new() { LabelType = "LOT", ItemId = m.Item }, None);
        var locations = await Service.TargetsAsync(new() { LabelType = "LOCATION", WarehouseId = m.Warehouse }, None);

        pallets.Should().ContainSingle().Which.Should().Be(
            new LabelTargetDto(m.Pallet, "PALLET", m.PalletNo, "Label Item", m.ItemCd, m.LotNo, 6m, Expiry, m.WarehouseCode, m.LocationCode));
        items.Should().ContainSingle().Which.Should().Be(
            new LabelTargetDto(m.Item, "ITEM", m.ItemCd, "Label Item", m.ItemCd, null, null, null, null, null));
        lots.Should().ContainSingle().Which.Should().Be(
            new LabelTargetDto(m.Lot, "LOT", m.LotNo, "Label Item", m.ItemCd, m.LotNo, null, Expiry, null, null));
        locations.Should().Contain(l => l.Id == m.Location && l.Code == m.LocationCode && l.Name == "Shelf")
            .And.NotContain(l => l.Code == "*");
    }

    [DatabaseFact, Trait("Id", "MM-UC-037-T03"), Trait("Id", "MM-UC-037-T04"), Trait("Rule", "MM-BR-919")]
    public async Task Print_Zpl_WritesOneHistoryRowPerTarget_ThatTheHistoryReadsWithItsText()
    {
        var m = await MastersAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var output = await Service.PrintAsync(
            new LabelPrintRequest("LOT", [new(m.Lot, 2)], "ZPL", "ZEBRA-1"), "kim", None);
        var history = await Service.HistoryAsync(new() { From = today.AddDays(-1), To = today.AddDays(1), LabelType = "LOT" }, None);

        Encoding.UTF8.GetString(output.Content).Should().Contain($"^FDLT|{m.ItemCd}|{m.LotNo}^FS");
        history.Should().ContainSingle(h => h.RefId == m.Lot).Which.Should().BeEquivalentTo(
            new { LabelType = "LOT", RefText = m.LotNo, Copies = 2, OutputKind = "ZPL", PrinterName = "ZEBRA-1", PrintedBy = "kim" });
    }

    private sealed record Masters(
        long Item, string ItemCd, long Lot, string LotNo, long Warehouse, string WarehouseCode, long Location, string LocationCode,
        long Pallet, string PalletNo);

    // An item with one Lot, a Location 'A<key>' and the system Location '*' on the default warehouse, an ACTIVE Pallet of 6 on
    // the Location and a CLOSED one
    private async Task<Masters> MastersAsync()
    {
        var key = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        var ids = await connection.QuerySingleAsync<(long Item, long Lot, long Warehouse, string WarehouseCode, long Location)>(
            """
            INSERT INTO bc_location (warehouse_id, location_code, is_system, created_by, updated_by)
            SELECT id, '*', true, 't', 't' FROM bc_warehouse WHERE is_default
            ON CONFLICT (warehouse_id, location_code) DO NOTHING;
            WITH i AS (INSERT INTO bc_item (item_cd, item_name, type, lot_managed, lot_managed_from, created_by, updated_by)
                       VALUES ('LB' || @Key, 'Label Item', 'INVENTORY', true, '2026-10-01', 't', 't') RETURNING id),
                 l AS (INSERT INTO mm_lot (item_id, lot_no, expiry_dt, created_by, updated_by)
                       SELECT id, 'L' || @Key, @Expiry, 't', 't' FROM i RETURNING id),
                 w AS (SELECT id, warehouse_code FROM bc_warehouse WHERE is_default),
                 loc AS (INSERT INTO bc_location (warehouse_id, location_code, location_name, created_by, updated_by)
                         SELECT id, 'A' || @Key, 'Shelf', 't', 't' FROM w RETURNING id)
            SELECT (SELECT id FROM i), (SELECT id FROM l), (SELECT id FROM w), (SELECT warehouse_code FROM w), (SELECT id FROM loc)
            """,
            new { Key = key, Expiry });
        var palletNo = "T" + key + "A";
        var pallet = await connection.QuerySingleAsync<long>(
            """
            INSERT INTO mm_pallet (pallet_no, item_id, lot_id, warehouse_id, location_id, qty, status, created_by, updated_by)
            VALUES (@ClosedNo, @Item, @Lot, @Warehouse, @Location, 0, 'CLOSED', 't', 't');
            INSERT INTO mm_pallet (pallet_no, item_id, lot_id, warehouse_id, location_id, qty, created_by, updated_by)
            VALUES (@PalletNo, @Item, @Lot, @Warehouse, @Location, 6, 't', 't') RETURNING id;
            """,
            new { ClosedNo = "T" + key + "C", PalletNo = palletNo, ids.Item, ids.Lot, ids.Warehouse, ids.Location });
        return new Masters(
            ids.Item, "LB" + key, ids.Lot, "L" + key, ids.Warehouse, ids.WarehouseCode, ids.Location, "A" + key, pallet, palletNo);
    }

    private sealed class NoPdf : ILabelPdfRenderer
    {
        public Task<byte[]> RenderAsync(LabelFormat format, IReadOnlyList<LabelPage> pages, int copies, CancellationToken cancellationToken) =>
            throw new NotSupportedException("These tests print ZPL.");
    }

    private sealed class UtcClock : IBusinessClock
    {
        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;

        public DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

        public DateTimeOffset ToCompanyTime(DateTimeOffset at) => at;

        public void Reset()
        {
        }
    }
}
