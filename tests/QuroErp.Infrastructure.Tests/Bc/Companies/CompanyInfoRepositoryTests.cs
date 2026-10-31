using System.Data;
using Dapper;
using FluentAssertions;
using QuroErp.Application.Bc.Companies;
using QuroErp.Application.Common.Auth;
using QuroErp.Application.Common.Persistence;
using QuroErp.Domain.Bc.Companies;
using QuroErp.Domain.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Bc.Companies;

/// <summary>Company information and security settings (BC-UC-007) on real PostgreSQL. Each test rolls back.</summary>
[Collection(AuthSchemaDefinition.Name)]
public class CompanyInfoRepositoryTests(AuthSchemaFixture db)
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static readonly CompanyFields Fields = new(
        "Quro Korea", "Seoul", "Busan", "Lee", "02-000-0000", null, "2208162517", "Kim", "info@quro.kr", "Service", "Software",
        "KR", "KRW", "ko", "Asia/Seoul", "SEPARATE");

    [DatabaseFact, Trait("Id", "BC-UC-007-IT-01")]
    public async Task Update_ChecksUpdatedAt_AndStoresBusinessTypeAndItem()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var companies = db.Get<ICompanyRepository>();
        await AddCompanyAsync(uow.Transaction);

        var loaded = (await companies.FindAsync(true, uow.Transaction, None))!;
        var stale = await companies.UpdateAsync(Fields, loaded.UpdatedAt.AddSeconds(-1), "kim", uow.Transaction, None);
        var saved = await companies.UpdateAsync(Fields, loaded.UpdatedAt, "kim", uow.Transaction, None);
        var after = (await companies.FindAsync(false, uow.Transaction, None))!;

        loaded.Name.Should().Be("Quro");
        loaded.BillingMode.Should().Be("WITH_SHIPMENT");
        stale.Should().BeFalse();
        saved.Should().BeTrue();
        (after.Name, after.Shipto, after.BusinessType, after.BusinessItem, after.BillingMode, after.HasLogo)
            .Should().Be(("Quro Korea", "Busan", "Service", "Software", "SEPARATE", false));
    }

    [DatabaseFact, Trait("Id", "CORE-050-IT-13")]
    public async Task Find_ReturnsTheFourAuditColumns_UpdateChangesOnlyTheUpdatedPair()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var companies = db.Get<ICompanyRepository>();
        await AddCompanyAsync(uow.Transaction);
        var loaded = (await companies.FindAsync(true, uow.Transaction, None))!;

        await companies.UpdateAsync(Fields, loaded.UpdatedAt, "kim", uow.Transaction, None);
        var after = (await companies.FindAsync(false, uow.Transaction, None))!;

        (loaded.CreatedBy, loaded.UpdatedBy).Should().Be(("system", "system"));
        loaded.CreatedAt.Should().NotBe(default);
        (after.CreatedAt, after.CreatedBy, after.UpdatedBy).Should().Be((loaded.CreatedAt, "system", "kim"));
    }

    [DatabaseFact, Trait("Id", "BC-UT-306")]
    public async Task DeleteLogo_ClearsImageAndType()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var companies = db.Get<ICompanyRepository>();
        (await companies.DeleteLogoAsync("kim", uow.Transaction, None)).Should().BeFalse();
        await AddCompanyAsync(uow.Transaction);
        await companies.UpdateLogoAsync([0x89, 0x50], "image/png", "kim", uow.Transaction, None);

        var deleted = await companies.DeleteLogoAsync("kim", uow.Transaction, None);

        deleted.Should().BeTrue();
        (await companies.FindLogoAsync(uow.Transaction, None)).Should().BeNull();
        (await companies.FindAsync(false, uow.Transaction, None))!.HasLogo.Should().BeFalse();
    }

    [DatabaseFact, Trait("Id", "BC-UT-312")]
    public async Task Transactions_StartWithTheFirstExchangeRate()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var companies = db.Get<ICompanyRepository>();
        var before = await companies.HasTransactionsAsync(uow.Transaction, None);

        await uow.Transaction.Connection!.ExecuteAsync(
            "INSERT INTO bc_exchange_rate (cur_dt, cur_rp, created_by, updated_by) VALUES ('2026-10-29', 1300, 't', 't')", transaction: uow.Transaction);

        before.Should().BeFalse();
        (await companies.HasTransactionsAsync(uow.Transaction, None)).Should().BeTrue();
    }

    [DatabaseFact, Trait("Id", "BC-UT-316")]
    public async Task UnbilledShipments_LeaveOutVoidAndFullyBilledLines()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var companies = db.Get<ICompanyRepository>();
        var connection = uow.Transaction.Connection!;
        var shipment = await connection.ExecuteScalarAsync<long>(
            """
            WITH c AS (INSERT INTO bc_customer (c_name, created_by, updated_by) VALUES ('Customer', 't', 't') RETURNING id),
                 w AS (INSERT INTO bc_warehouse (warehouse_code, warehouse_name, created_by, updated_by) VALUES ('ZW1', 'W', 't', 't') RETURNING id)
            INSERT INTO sd_shipment (ship_no, customer_id, ship_dt, warehouse_id, currency_code, exchange_rate, created_by, updated_by)
            SELECT 'ZS1', c.id, '2026-10-29', w.id, 'USD', 1, 't', 't' FROM c, w
            RETURNING id
            """,
            transaction: uow.Transaction);
        await connection.ExecuteAsync(
            """
            WITH i AS (INSERT INTO bc_item (item_cd, created_by, updated_by) VALUES ('ZI1', 't', 't') RETURNING id)
            INSERT INTO sd_shipment_line (shipment_id, seq, item_id, warehouse_id, ship_qty, ship_prc, billed_qty, created_by, updated_by)
            SELECT @shipment, 1, i.id, s.warehouse_id, 5, 1, 5, 't', 't' FROM i, sd_shipment s WHERE s.id = @shipment
            """,
            new { shipment }, uow.Transaction);
        var allBilled = await companies.HasUnbilledShipmentsAsync(uow.Transaction, None);

        await connection.ExecuteAsync("UPDATE sd_shipment_line SET billed_qty = 2 WHERE shipment_id = @shipment", new { shipment }, uow.Transaction);
        var unbilled = await companies.HasUnbilledShipmentsAsync(uow.Transaction, None);
        await connection.ExecuteAsync("UPDATE sd_shipment SET status = 'VO' WHERE id = @shipment", new { shipment }, uow.Transaction);
        var voided = await companies.HasUnbilledShipmentsAsync(uow.Transaction, None);

        allBilled.Should().BeFalse();
        unbilled.Should().BeTrue();
        voided.Should().BeFalse();
    }

    [DatabaseFact, Trait("Id", "BC-UT-310")]
    public async Task AuthSetting_UpdateChecksUpdatedAt_AndRevokeEveryUserRevokesOpenTokens()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var settings = db.Get<IAuthSettingRepository>();
        var row = (await settings.FindForEditAsync(true, uow.Transaction, None))!;
        var next = new AuthSetting { MaxFailedAttempts = 4, AutoLoginEnabled = false, PasswordResetMode = "EMAIL" };

        var stale = await settings.UpdateAsync(next, row.UpdatedAt.AddSeconds(-1), "kim", uow.Transaction, None);
        var saved = await settings.UpdateAsync(next, row.UpdatedAt, "kim", uow.Transaction, None);
        var after = (await settings.FindForEditAsync(false, uow.Transaction, None))!;
        await db.Get<IAutoLoginTokenRepository>().RevokeEveryUserAsync(DateTimeOffset.UtcNow, uow.Transaction, None);
        var open = await uow.Transaction.Connection!.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM sys_auto_login_token WHERE revoked_at IS NULL", transaction: uow.Transaction);

        stale.Should().BeFalse();
        saved.Should().BeTrue();
        (after.Setting.MaxFailedAttempts, after.Setting.PasswordResetMode).Should().Be((4, "EMAIL"));
        open.Should().Be(0);
    }

    private static Task<int> AddCompanyAsync(IDbTransaction transaction) =>
        transaction.Connection!.ExecuteAsync(
            """
            INSERT INTO bc_company (name, country_code, base_currency_code, default_lang_code, time_zone, created_by, updated_by)
            VALUES ('Quro', 'KR', 'KRW', 'ko', 'Asia/Seoul', 'system', 'system')
            """,
            transaction: transaction);
}
