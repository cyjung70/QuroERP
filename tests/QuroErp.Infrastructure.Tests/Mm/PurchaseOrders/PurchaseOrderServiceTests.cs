using System.Text.RegularExpressions;
using Dapper;
using FluentAssertions;
using Npgsql;
using QuroErp.Application.Bc.BusinessPlaces;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Bc.Tax;
using QuroErp.Application.Common;
using QuroErp.Application.Common.DocumentNumbers;
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Mm.PurchaseOrders;
using QuroErp.Contracts.Mm.PurchaseOrders;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.PurchaseOrders;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Mm.PurchaseOrders;

/// <summary>Own schema for purchase order tests: the service commits, so its rows must not reach other test classes.</summary>
[CollectionDefinition(Name)]
public sealed class PurchaseOrderSchemaDefinition : ICollectionFixture<AuthSchemaFixture>
{
    public const string Name = "PurchaseOrderSchema";
}

/// <summary>
/// PurchaseOrderService with the Dapper repositories on real PostgreSQL (MM-UC-003). Without a company the base currency is USD.
/// Each test makes its own vendor and items.
/// </summary>
[Collection(PurchaseOrderSchemaDefinition.Name)]
public partial class PurchaseOrderServiceTests(AuthSchemaFixture db)
{
    private static readonly DateOnly PoDt = new(2026, 10, 30);
    private static readonly CancellationToken None = CancellationToken.None;

    private PurchaseOrderService Service => new(
        db.Get<IPurchaseOrderRepository>(), db.Get<ITaxRepository>(), new TaxAreaRateProvider(db.Get<ITaxRepository>()),
        db.Get<ILocalizationRepository>(), db.Get<IBusinessPlaceRepository>(), new DocumentNumberService(db.Get<IDocumentNumberRepository>()),
        db.Get<IUnitOfWorkFactory>());

    [DatabaseFact, Trait("Id", "MM-UC-003-T01"), Trait("Id", "MM-UC-003-T07")]
    public async Task Create_SavesADraftWithANumber_AndServerAmounts()
    {
        var m = await MastersAsync();

        var po = await Service.CreateAsync(Request(m, Line(m.Item, dcRate: 10m), Line(m.Service, qty: 2m, price: 5m, taxCode: m.NoTax)), "kim", None);

        po.Status.Should().Be(PoStatusCode.Draft);
        PoNumber().IsMatch(po.PoNo).Should().BeTrue(po.PoNo);
        po.Lines.Select(l => (l.Seq, l.GrossAmt, l.DcAmt, l.PoAmt, l.TaxAmt, l.WarehouseId, l.DueDt))
            .Should().Equal((1, 1000m, 100m, 900m, 90m, m.Warehouse, PoDt.AddDays(7)), (2, 10m, 0m, 10m, 0m, m.Warehouse, PoDt.AddDays(7)));
        (po.GrossAmt, po.DcAmt, po.SupplyAmt, po.TaxAmt, po.TotPoAmt, po.TotBaseAmt, po.CurrencyCode, po.RevisionNo)
            .Should().Be((1010m, 100m, 910m, 90m, 1000m, 1000m, "USD", 0));
        (await Service.SearchAsync(new(po.PoNo[..6], m.Vendor, PoStatusCode.Draft, PoDt, PoDt), None))
            .Should().ContainSingle(s => s.Id == po.Id && s.VendorName == m.VendorName);
    }

    [DatabaseFact, Trait("Id", "MM-UC-003-T02")]
    public async Task Approve_NeedsLines_ThenSetsPoAndWhoApproved()
    {
        var m = await MastersAsync();
        var empty = await Service.CreateAsync(Request(m), "kim", None);
        var po = await Service.CreateAsync(Request(m, Line(m.Item)), "kim", None);

        var noLines = () => Service.ApproveAsync(empty.Id, empty.UpdatedAt, "lee", None);
        var approved = await Service.ApproveAsync(po.Id, po.UpdatedAt, "lee", None);

        (await noLines.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(PoStatusRules.NotApprovableCode);
        (approved.Status, approved.ApprovedBy, approved.ApprovedAt.HasValue).Should().Be((PoStatusCode.Approved, "lee", true));
        approved.PoNo.Should().Be(po.PoNo);
    }

    [DatabaseFact, Trait("Id", "MM-UC-003-T03")]
    public async Task UpdateApproved_StaysApproved_AndCountsRevisions_AndReplacesLines()
    {
        var m = await MastersAsync();
        var draft = await Service.CreateAsync(Request(m, Line(m.Item), Line(m.Service, price: 5m)), "kim", None);
        var po = await Service.ApproveAsync(draft.Id, draft.UpdatedAt, "lee", None);

        // Second line removed, first changed, a new one added at the end
        var first = po.Lines[0];
        var once = await Service.UpdateAsync(po.Id, Request(m, po.UpdatedAt,
            Line(m.Item, qty: 3m) with { Id = first.Id }, Line(m.Item, price: 7m)), "kim", None);
        var twice = await Service.UpdateAsync(po.Id, Request(m, once.UpdatedAt, [.. once.Lines.Select(ToRequest)]), "kim", None);

        (once.Status, once.RevisionNo, twice.RevisionNo).Should().Be((PoStatusCode.Approved, 1, 2));
        once.Lines.Select(l => (l.Id == first.Id, l.Seq, l.PoQty, l.PoPrc)).Should().Equal((true, 1, 3m, 100m), (false, 2, 10m, 7m));
    }

    [DatabaseFact, Trait("Id", "MM-UC-003-T04"), Trait("Id", "MM-UT-013"), Trait("Id", "MM-UT-023")]
    public async Task AfterAReceipt_EditDeleteAndCancelAre422_ButCloseWorks()
    {
        var m = await MastersAsync();
        var draft = await Service.CreateAsync(Request(m, Line(m.Item)), "kim", None);
        var po = await Service.ApproveAsync(draft.Id, draft.UpdatedAt, "lee", None);
        await ReceiveAsync(po.Lines[0].Id, m.NoTax, 1m);
        po = await Service.GetAsync(po.Id, None);

        var update = () => Service.UpdateAsync(po.Id, Request(m, po.UpdatedAt, Line(m.Item)), "kim", None);
        var delete = () => Service.DeleteAsync(po.Id, po.UpdatedAt, None);
        var cancel = () => Service.CancelAsync(po.Id, po.UpdatedAt, "kim", None);

        po.ReceivedQty.Should().Be(1m);
        (await update.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(PoStatusRules.NotEditableCode);
        (await delete.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(PoStatusRules.NotEditableCode);
        (await cancel.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(PoStatusRules.NotCancellableCode);
        var closed = await Service.CloseAsync(po.Id, po.UpdatedAt, "park", None);
        (closed.Status, closed.ClosedBy).Should().Be((PoStatusCode.Closed, "park"));
    }

    [DatabaseFact, Trait("Id", "MM-UT-023")]
    public async Task Cancel_ApprovedWithoutReceipts_AndDelete_Draft()
    {
        var m = await MastersAsync();
        var first = await Service.CreateAsync(Request(m, Line(m.Item)), "kim", None);
        var approved = await Service.ApproveAsync(first.Id, first.UpdatedAt, "lee", None);
        var draft = await Service.CreateAsync(Request(m, Line(m.Item)), "kim", None);

        var cancelled = await Service.CancelAsync(approved.Id, approved.UpdatedAt, "park", None);
        await Service.DeleteAsync(draft.Id, draft.UpdatedAt, None);

        (cancelled.Status, cancelled.CancelledBy).Should().Be((PoStatusCode.Cancelled, "park"));
        var gone = () => Service.GetAsync(draft.Id, None);
        await gone.Should().ThrowAsync<NotFoundException>();
    }

    [DatabaseFact, Trait("Id", "MM-UC-003-T04")]
    public async Task StaleUpdatedAt_Is409()
    {
        var m = await MastersAsync();
        var po = await Service.CreateAsync(Request(m, Line(m.Item)), "kim", None);
        await Service.UpdateAsync(po.Id, Request(m, po.UpdatedAt, Line(m.Item, qty: 2m)), "kim", None);

        var update = () => Service.UpdateAsync(po.Id, Request(m, po.UpdatedAt, Line(m.Item, qty: 3m)), "kim", None);
        var approve = () => Service.ApproveAsync(po.Id, po.UpdatedAt, "lee", None);

        await update.Should().ThrowAsync<ConcurrencyException>();
        await approve.Should().ThrowAsync<ConcurrencyException>();
    }

    [DatabaseFact, Trait("Id", "MM-UC-003-T09")]
    public async Task VendorDefaults_FillCurrencyAndLineTaxCode()
    {
        var m = await MastersAsync();

        var po = await Service.CreateAsync(Request(m, Line(m.Item) with { TaxCodeId = null }) with { CurrencyCode = null }, "kim", None);

        (po.CurrencyCode, po.Lines[0].TaxCodeId).Should().Be(("USD", m.Vat10));
    }

    [DatabaseFact, Trait("Id", "CORE-046-IT-05"), Trait("Rule", "BC-BR-321")]
    public async Task Create_GivesTheNewOrderTheDefaultBusinessPlace()
    {
        var m = await MastersAsync();

        var po = await Service.CreateAsync(Request(m), "kim", None);

        await using var connection = new NpgsqlConnection(db.ConnectionString);
        (await connection.QuerySingleAsync<bool>(
                "SELECT business_place_id = (SELECT id FROM bc_business_place WHERE is_default) FROM mm_purchase_order WHERE id = @Id",
                new { po.Id }))
            .Should().BeTrue();
    }

    [DatabaseFact, Trait("Id", "CORE-046-IT-06"), Trait("Rule", "BC-BR-321")]
    public async Task Create_WithoutADefaultBusinessPlace_Is400BcE321()
    {
        var m = await MastersAsync();
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        var place = await connection.QuerySingleAsync<long>("UPDATE bc_business_place SET is_default = false WHERE is_default RETURNING id");
        try
        {
            var act = () => Service.CreateAsync(Request(m), "kim", None);

            (await act.Should().ThrowAsync<BadRequestException>()).Which.Code.Should().Be("BC-E-321");
        }
        finally
        {
            await connection.ExecuteAsync("UPDATE bc_business_place SET is_default = true WHERE id = @Place", new { Place = place });
        }
    }

    [DatabaseFact, Trait("Rule", "BC-BR-424"), Trait("Rule", "BC-BR-545")]
    public async Task MmRoundingDown_CutsTheSavedAmountsAndTax()
    {
        var m = await MastersAsync();
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        var before = await connection.QuerySingleAsync<string>("SELECT rounding_mode FROM sys_decimal_setting WHERE module = 'MM'");
        await connection.ExecuteAsync("UPDATE sys_decimal_setting SET rounding_mode = 'DOWN' WHERE module = 'MM'");
        try
        {
            var po = await Service.CreateAsync(Request(m, Line(m.Item, qty: 1m, price: 100.055m)), "kim", None);

            // HALF_UP would give 100.06 and 10.01 (10.006)
            (po.Lines[0].GrossAmt, po.Lines[0].TaxAmt, po.TotPoAmt).Should().Be((100.05m, 10.00m, 110.05m));
        }
        finally
        {
            await connection.ExecuteAsync("UPDATE sys_decimal_setting SET rounding_mode = @Before WHERE module = 'MM'", new { Before = before });
        }
    }

    [DatabaseFact, Trait("Id", "MM-UT-014")]
    public async Task RuleViolation_SavesNothing()
    {
        var m = await MastersAsync();

        var act = () => Service.CreateAsync(Request(m, Line(m.Item), Line(m.Item, qty: 0m)), "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal(2);
        (await Service.SearchAsync(new(null, m.Vendor, null, null, null), None)).Should().BeEmpty();
    }

    private static SavePurchaseOrderRequest Request(Masters m, params SavePurchaseOrderLineRequest[] lines) => Request(m, null, lines);

    private static SavePurchaseOrderRequest Request(Masters m, DateTimeOffset? updatedAt, params SavePurchaseOrderLineRequest[] lines) =>
        new(m.Vendor, PoDt, "USD", null, "FOB", PoDt.AddDays(7), m.Warehouse, "NET30", "kim", null, lines, updatedAt);

    private static SavePurchaseOrderLineRequest Line(long item, decimal qty = 10m, decimal price = 100m, decimal dcRate = 0m, long? taxCode = null) =>
        new(null, item, null, qty, price, dcRate, null, null, taxCode);

    private static SavePurchaseOrderLineRequest ToRequest(PurchaseOrderLineDto l) =>
        new(l.Id, l.ItemId, l.ItemName, l.PoQty, l.PoPrc, l.DcRate, l.DueDt, l.WarehouseId, l.TaxCodeId);

    private sealed record Masters(long Vendor, string VendorName, long Item, long Service, long Warehouse, long Vat10, long NoTax);

    // A vendor whose default tax code is a 10% VAT code, an inventory and a service item, and the default warehouse
    private async Task<Masters> MastersAsync()
    {
        var key = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        var ids = await connection.QuerySingleAsync<(long Vat10, long NoTax, long Vendor, long Item, long Service, long Warehouse)>(
            """
            WITH t AS (INSERT INTO bc_tax_code (tax_code, tax_kind, rate, created_by, updated_by)
                       VALUES ('V' || @Key, 'VAT_STANDARD', 10, 't', 't') RETURNING id),
                 v AS (INSERT INTO bc_vendor (v_name, currency_code, tax_code_id, created_by, updated_by)
                       SELECT 'Vendor ' || @Key, 'USD', id, 't', 't' FROM t RETURNING id),
                 i AS (INSERT INTO bc_item (item_cd, item_name, type, created_by, updated_by)
                       VALUES ('I' || @Key, 'Bolt', 'INVENTORY', 't', 't') RETURNING id),
                 s AS (INSERT INTO bc_item (item_cd, item_name, type, created_by, updated_by)
                       VALUES ('S' || @Key, 'Freight', 'SERVICE', 't', 't') RETURNING id)
            SELECT (SELECT id FROM t), (SELECT id FROM bc_tax_code WHERE tax_code = 'NONE'), (SELECT id FROM v), (SELECT id FROM i),
                   (SELECT id FROM s), (SELECT min(id) FROM bc_warehouse WHERE use_yn)
            """,
            new { Key = key });
        return new Masters(ids.Vendor, "Vendor " + key, ids.Item, ids.Service, ids.Warehouse, ids.Vat10, ids.NoTax);
    }

    private async Task ReceiveAsync(long purchaseOrderLineId, long taxCodeId, decimal qty)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.ExecuteAsync(
            """
            WITH g AS (INSERT INTO mm_goods_receipt (gr_no, warehouse_id, currency_code, exchange_rate, business_place_id, created_by, updated_by)
                       VALUES ('GR' || @Line, (SELECT min(id) FROM bc_warehouse), 'USD', 1, (SELECT id FROM bc_business_place WHERE is_default), 't', 't') RETURNING id)
            INSERT INTO mm_goods_receipt_line (goods_receipt_id, seq, purchase_order_line_id, gr_qty, warehouse_id, tax_code_id, created_by, updated_by)
            SELECT id, 1, @Line, @Qty, (SELECT min(id) FROM bc_warehouse), @Tax, 't', 't' FROM g
            """,
            new { Line = purchaseOrderLineId, Qty = qty, Tax = taxCodeId });
    }

    [GeneratedRegex(@"^PO2610\d{5}$")]
    private static partial Regex PoNumber();
}
