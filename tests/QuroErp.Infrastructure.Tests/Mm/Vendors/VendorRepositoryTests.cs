using Dapper;
using FluentAssertions;
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Mm.Vendors;
using QuroErp.Domain.Mm.Vendors;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Mm.Vendors;

/// <summary>Vendors against PostgreSQL (MM-UC-001). Each test rolls back by not committing.</summary>
[Collection(AuthSchemaDefinition.Name)]
public class VendorRepositoryTests(AuthSchemaFixture db)
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static readonly VendorFields Fields = new(
        Phone1: "555", ZipCode: "1", ShipZipCode: "1", Terms: "NET_30", ValidFlag: "Y", BusinessRegNo: "2208162517");

    [DatabaseFact, Trait("Id", "MM-UC-001-IT-01")]
    public async Task InsertFindSearchAdjacentUpdateAndDelete_RoundTrip()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<IVendorRepository>();
        var connection = uow.Transaction.Connection!;
        var noTax = await connection.QuerySingleAsync<long>("SELECT id FROM bc_tax_code WHERE tax_code = 'NONE'", transaction: uow.Transaction);

        var a = await repository.InsertAsync(
            Vendor.Restore(0, "ZZQV_A", Fields with { VendorPrice = "ZZQV_A", TaxCodeId = noTax, Remarks = "Memo" }), "kim", uow.Transaction, None);
        var b = await repository.InsertAsync(Vendor.Restore(0, "ZZQV_B", Fields with { VendorPrice = "ZZQV_B" }), "kim", uow.Transaction, None);
        var found = await repository.FindAsync(a, forUpdate: true, uow.Transaction, None);
        var search = await repository.SearchAsync(new VendorSearch("zzqv_", "Y", 0, 50), uow.Transaction, None);
        var next = await repository.FindAdjacentAsync(a, forward: true, uow.Transaction, None);
        var previous = await repository.FindAdjacentAsync(b, forward: false, uow.Transaction, None);
        var sameNumber = await repository.FindNamesByBusinessRegNoAsync("2208162517", a, uow.Transaction, None);
        var updated = await repository.UpdateAsync(
            Vendor.Restore(a, "ZZQV_A", found!.Fields with { Contact = "Lee" }), found.UpdatedAt, "lee", uow.Transaction, None);
        var stale = await repository.UpdateAsync(
            Vendor.Restore(a, "ZZQV_A", found.Fields), found.UpdatedAt.AddSeconds(-1), "lee", uow.Transaction, None);
        var afterUpdate = await repository.FindAsync(a, forUpdate: false, uow.Transaction, None);
        var staleDelete = await repository.DeleteAsync(b, found.UpdatedAt.AddSeconds(-1), uow.Transaction, None);
        var deleted = await repository.DeleteAsync(b, (await repository.FindAsync(b, false, uow.Transaction, None))!.UpdatedAt, uow.Transaction, None);

        found.VName.Should().Be("ZZQV_A");
        found.TaxCode.Should().Be("NONE");
        found.Fields.Remarks.Should().Be("Memo");
        found.BalAmt.Should().Be(0);
        (await repository.NameExistsAsync("ZZQV_A", uow.Transaction, None)).Should().BeTrue();
        search.Items.Select(v => v.VName).Should().Equal("ZZQV_A", "ZZQV_B");
        search.Total.Should().Be(2);
        next!.Id.Should().Be(b);
        previous!.Id.Should().Be(a);
        sameNumber.Should().Contain("ZZQV_B").And.NotContain("ZZQV_A");
        updated.Should().BeTrue();
        stale.Should().BeFalse();
        (afterUpdate!.Fields.Contact, afterUpdate.UpdatedBy).Should().Be(("Lee", "lee"));
        staleDelete.Should().BeFalse();
        deleted.Should().BeTrue();
        (await repository.FindAsync(b, false, uow.Transaction, None)).Should().BeNull();
    }

    [DatabaseFact, Trait("Id", "MM-UT-851"), Trait("Rule", "MM-BR-851")]
    public async Task DefaultTerms_IsASeededTermsCode()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);

        var exists = await uow.Transaction.Connection!.ExecuteScalarAsync<bool>(
            """
            SELECT EXISTS (SELECT 1 FROM sys_code_minor n JOIN sys_code_major m ON m.id = n.major_id
                           WHERE m.major_code = 'TERMS' AND n.minor_code = @Code)
            """,
            new { Code = Vendor.DefaultTerms },
            uow.Transaction);

        exists.Should().BeTrue();
    }

    [DatabaseFact, Trait("Id", "MM-UT-858"), Trait("Rule", "MM-BR-858")]
    public async Task Balance_IsZero_GoodsReceiptsHaveNoPayable()
    {
        // Goods receipts no longer carry a balance (TASK-MM-007); supplier invoices will (MM-BR-755)
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<IVendorRepository>();
        var id = await repository.InsertAsync(Vendor.Restore(0, "ZZQV_BAL", Fields), "kim", uow.Transaction, None);

        await ReceiveAsync(uow, id, "ZZQGR1");

        (await repository.FindAsync(id, false, uow.Transaction, None))!.BalAmt.Should().Be(0m);
    }

    [DatabaseFact, Trait("Id", "MM-UT-857"), Trait("Rule", "MM-BR-857")]
    public async Task InUse_ByGoodsReceipt_OrByAPriceGroupNoOtherVendorShares()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<IVendorRepository>();
        var connection = uow.Transaction.Connection!;
        var received = await repository.InsertAsync(Vendor.Restore(0, "ZZQV_GR", Fields with { VendorPrice = "ZZQV_GR" }), "kim", uow.Transaction, None);
        var priced = await repository.InsertAsync(Vendor.Restore(0, "ZZQV_PR", Fields with { VendorPrice = "ZZQP" }), "kim", uow.Transaction, None);
        var unused = await repository.InsertAsync(Vendor.Restore(0, "ZZQV_NO", Fields with { VendorPrice = "ZZQV_NO" }), "kim", uow.Transaction, None);
        await ReceiveAsync(uow, received, "ZZQGR3");
        await connection.ExecuteAsync(
            "INSERT INTO bc_purchase_price (vendor_price, purchase_prc, created_by, updated_by) VALUES ('ZZQP', 1, 't', 't')",
            transaction: uow.Transaction);

        var pricedAlone = await repository.IsInUseAsync(priced, uow.Transaction, None);
        await repository.InsertAsync(Vendor.Restore(0, "ZZQV_PR2", Fields with { VendorPrice = "ZZQP" }), "kim", uow.Transaction, None);
        var pricedShared = await repository.IsInUseAsync(priced, uow.Transaction, None);

        (await repository.IsInUseAsync(received, uow.Transaction, None)).Should().BeTrue();
        pricedAlone.Should().BeTrue();
        pricedShared.Should().BeFalse("another vendor still uses the price group");
        (await repository.IsInUseAsync(unused, uow.Transaction, None)).Should().BeFalse();
    }

    private static Task<int> ReceiveAsync(IUnitOfWork uow, long vendorId, string grNo) =>
        uow.Transaction.Connection!.ExecuteAsync(
            """
            INSERT INTO mm_goods_receipt (gr_no, vendor_id, warehouse_id, currency_code, exchange_rate, business_place_id, created_by, updated_by)
            VALUES (@GrNo, @VendorId, (SELECT min(id) FROM bc_warehouse), 'USD', 1, (SELECT id FROM bc_business_place WHERE is_default), 't', 't')
            """,
            new { GrNo = grNo, VendorId = vendorId },
            uow.Transaction);
}
