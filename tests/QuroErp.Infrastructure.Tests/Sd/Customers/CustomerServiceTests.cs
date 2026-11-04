using Dapper;
using FluentAssertions;
using Npgsql;
using QuroErp.Application.Bc.Companies;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Bc.Tax;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Codes;
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Sd.Customers;
using QuroErp.Contracts.Common;
using QuroErp.Contracts.Sd.Customers;
using QuroErp.Domain.Common;
using QuroErp.Domain.Sd.Customers;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Sd.Customers;

/// <summary>Own schema for customer tests: the service commits, and some tests set the company country.</summary>
[CollectionDefinition(Name)]
public sealed class CustomerSchemaDefinition : ICollectionFixture<AuthSchemaFixture>
{
    public const string Name = "CustomerSchema";
}

/// <summary>
/// CustomerService with the Dapper repository on real PostgreSQL (SD-UC-001). Without a company the country is US. Each test
/// makes its own customers; the sales rep KIM is added to SALES_REP.
/// </summary>
[Collection(CustomerSchemaDefinition.Name)]
public class CustomerServiceTests : IAsyncLifetime
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly DateOnly Today = new(2026, 11, 5);
    private readonly AuthSchemaFixture _db;

    public CustomerServiceTests(AuthSchemaFixture db) => _db = db;

    private CustomerService Service => new(
        _db.Get<ICustomerRepository>(), _db.Get<ICompanyRepository>(), _db.Get<ILocalizationRepository>(), _db.Get<ITaxCodeRepository>(),
        new CodeValidator(_db.Get<ICodeRepository>(), _db.Get<IUnitOfWorkFactory>()), new FixedClock(), _db.Get<IUnitOfWorkFactory>());

    public Task InitializeAsync() =>
        ExecuteAsync(
            """
            INSERT INTO sys_code_minor (major_id, minor_code, sort_order, created_by, updated_by)
            SELECT m.id, 'KIM', 10, 't', 't' FROM sys_code_major m
            WHERE m.major_code = 'SALES_REP' AND NOT EXISTS (
                SELECT 1 FROM sys_code_minor n WHERE n.major_id = m.id AND n.minor_code = 'KIM')
            """);

    public Task DisposeAsync() => Task.CompletedTask;

    [DatabaseFact, Trait("Id", "SD-UC-001-T01"), Trait("Id", "SD-UT-302"), Trait("Id", "SD-UT-303"), Trait("Id", "SD-UT-304"),
     Trait("Id", "SD-UT-308"), Trait("Id", "SD-UT-335")]
    public async Task Create_SavesTheNormalizedCustomerWithItsDefaults_AndSearchFindsIt()
    {
        var name = NewName();

        var created = await Service.CreateAsync(New(" " + name.ToLowerInvariant() + " "), "kim", None);

        (created.CName, created.ShipName, created.Terms, created.ValidFlag, created.PriceLevel, created.ShipZipCode, created.StartDt)
            .Should().Be((name, "SHIP TO", "COD_CHECK", "Y", name, "90001", (DateOnly?)Today));
        (created.BillingFlow, created.BalAmt, created.CreditLimit, created.TaxCode, created.CreatedBy)
            .Should().Be(("SHIP_AND_BILL", 0m, (decimal?)0m, "NONE", "kim"));
        (await Service.GetAsync(created.Id, None)).Should().BeEquivalentTo(created with { Warnings = [] });
        (await Service.SearchAsync(new CustomerSearchQuery { CName = name[^6..].ToLowerInvariant() }, None)).Items
            .Should().ContainSingle(c => c.Id == created.Id);
    }

    [DatabaseFact, Trait("Id", "SD-UC-001-T02"), Trait("Id", "SD-UT-301"), Trait("Id", "SD-UT-302"), Trait("Id", "SD-UT-331")]
    public async Task MissingValues_DuplicateNames_AndAUsCustomerWithoutTaxCode_AreRefused()
    {
        var name = NewName();
        await Service.CreateAsync(New(name), "kim", None);

        (await CodeOfAsync(() => Service.CreateAsync(New(NewName()) with { Phone1 = " " }, "kim", None))).Should().Be(Customer.RequiredCode);
        (await CodeOfAsync(() => Service.CreateAsync(New(name.ToLowerInvariant()), "kim", None))).Should().Be(Customer.DuplicateNameCode);
        (await CodeOfAsync(() => Service.CreateAsync(New(NewName()) with { TaxCodeId = null }, "kim", None)))
            .Should().Be(Customer.UsTaxCodeRequiredCode);
    }

    [DatabaseFact, Trait("Id", "SD-UC-001-T04"), Trait("Id", "SD-UT-305"), Trait("Id", "BC-UT-415"), Trait("Id", "BC-UT-523")]
    public async Task UnknownCodesCurrenciesAndTaxCodes_AreRefused()
    {
        (await CodeOfAsync(() => Service.CreateAsync(New(NewName()) with { FreightCharge = "COLLECT" }, "kim", None)))
            .Should().Be(CodeMessages.InvalidCode);
        (await CodeOfAsync(() => Service.CreateAsync(New(NewName()) with { Rep = "NOBODY" }, "kim", None))).Should().Be(CodeMessages.InvalidCode);
        (await CodeOfAsync(() => Service.CreateAsync(New(NewName()) with { DcStatus = "HALF" }, "kim", None))).Should().Be(CodeMessages.InvalidCode);
        (await CodeOfAsync(() => Service.CreateAsync(New(NewName()) with { CurrencyCode = "XYZ" }, "kim", None))).Should().Be(Customer.CurrencyCode);
        (await CodeOfAsync(() => Service.CreateAsync(New(NewName()) with { TaxCodeId = -1 }, "kim", None))).Should().Be(Customer.TaxCodeCode);
        (await Service.CreateAsync(New(NewName()) with { FreightCharge = "N" }, "kim", None)).FreightCharge.Should().Be("N");
    }

    [DatabaseFact, Trait("Id", "SD-UC-001-T03")]
    public async Task Update_KeepsTheName_AndAStaleUpdatedAtIs409()
    {
        var created = await Service.CreateAsync(New(NewName()), "kim", None);

        var updated = await Service.UpdateAsync(created.Id, Change(created) with { Phone1 = "555-0199", BillingFlow = "bill_then_ship" }, "lee", None);
        var stale = () => Service.UpdateAsync(created.Id, Change(created), "lee", None);

        (updated.CName, updated.Phone1, updated.BillingFlow, updated.UpdatedBy).Should().Be((created.CName, "555-0199", "BILL_THEN_SHIP", "lee"));
        await stale.Should().ThrowAsync<ConcurrencyException>();
    }

    [DatabaseFact, Trait("Id", "SD-UT-307"), Trait("Id", "SD-UT-308")]
    public async Task ACustomerWithInvoices_ShowsTheirBalance_AndCannotBeDeleted()
    {
        var withInvoices = await Service.CreateAsync(New(NewName()), "kim", None);
        var unused = await Service.CreateAsync(New(NewName()), "kim", None);
        await InvoiceAsync(withInvoices.Id, 120m, "OP");
        await InvoiceAsync(withInvoices.Id, -20m, "OP");
        await InvoiceAsync(withInvoices.Id, 999m, "VO");
        await Service.SaveTaxExemptionsAsync(unused.Id, new([new(null, "EX-1", "CA", null, null)], [], []), "kim", None);

        (await Service.GetAsync(withInvoices.Id, None)).BalAmt.Should().Be(100m);
        var blocked = await FluentActions.Invoking(() => Service.DeleteAsync(withInvoices.Id, withInvoices.UpdatedAt, None))
            .Should().ThrowAsync<DomainException>();
        blocked.Which.Code.Should().Be(Customer.InUseCode);
        await Service.DeleteAsync(unused.Id, unused.UpdatedAt, None);
        await FluentActions.Invoking(() => Service.GetAsync(unused.Id, None)).Should().ThrowAsync<NotFoundException>();
    }

    [DatabaseFact, Trait("Id", "SD-UT-333"), Trait("Id", "BC-UT-524")]
    public async Task TaxExemptions_AreAddedChangedAndDeletedTogether()
    {
        var customer = await Service.CreateAsync(New(NewName()), "kim", None);
        var first = await Service.SaveTaxExemptionsAsync(customer.Id, new(
            [new(null, "CA-1", "ca", Today.AddDays(-1), null), new(null, "CA-2", "CA", null, null), new(null, "TX-1", "TX", null, null)],
            [], []), "kim", None);
        first.Select(e => (e.CertificateNo, e.StateCode)).Should().Equal(("CA-1", "CA"), ("CA-2", "CA"), ("TX-1", "TX"));

        var tx1 = first.Single(e => e.CertificateNo == "TX-1");
        var expired = first.Single(e => e.CertificateNo == "CA-1");
        var saved = await Service.SaveTaxExemptionsAsync(customer.Id, new(
            [new(null, "TX-OLD", "TX", Today.AddYears(-1), null)],
            [new(tx1.Id, "TX-1A", "TX", Today.AddYears(1), tx1.UpdatedAt)],
            [new(expired.Id, expired.UpdatedAt)]), "kim", None);

        saved.Select(e => (e.CertificateNo, e.StateCode, e.ValidTo))
            .Should().Equal(("CA-2", "CA", null), ("TX-1A", "TX", Today.AddYears(1)), ("TX-OLD", "TX", Today.AddYears(-1)));
        (await Service.GetTaxExemptionsAsync(customer.Id, None)).Should().BeEquivalentTo(saved);
    }

    [DatabaseFact, Trait("Id", "SD-UT-333"), Trait("Id", "BC-UT-524")]
    public async Task TaxExemptions_WithTwoValidForAState_OrRowsOfAnotherCustomer_ChangeNothing()
    {
        var customer = await Service.CreateAsync(New(NewName()), "kim", None);
        var other = await Service.CreateAsync(New(NewName()), "kim", None);
        var kept = await Service.SaveTaxExemptionsAsync(customer.Id, new([new(null, "CA-1", "CA", null, null)], [], []), "kim", None);
        var foreign = (await Service.SaveTaxExemptionsAsync(other.Id, new([new(null, "TX-9", "TX", null, null)], [], []), "kim", None)).Single();

        (await CodeOfAsync(() => Service.SaveTaxExemptionsAsync(
            customer.Id, new([new(null, "CA-2", "CA", Today, null)], [], []), "kim", None))).Should().Be(CustomerTaxExemptions.InvalidCode);
        (await CodeOfAsync(() => Service.SaveTaxExemptionsAsync(
            customer.Id, new([], [], [new(foreign.Id, foreign.UpdatedAt)]), "kim", None))).Should().Be(CustomerTaxExemptions.NotOfCustomerCode);
        (await CodeOfAsync(() => Service.SaveTaxExemptionsAsync(
            customer.Id, new([new(null, "AZ-1", "ZZ", null, null)], [], []), "kim", None))).Should().Be(CodeMessages.InvalidCode);
        var stale = () => Service.SaveTaxExemptionsAsync(
            customer.Id, new([], [new(kept[0].Id, "CA-1", "CA", Today, kept[0].UpdatedAt.AddSeconds(-1))], []), "kim", None);

        await stale.Should().ThrowAsync<ConcurrencyException>();
        (await Service.GetTaxExemptionsAsync(customer.Id, None)).Should().BeEquivalentTo(kept);
    }

    [DatabaseFact, Trait("Id", "SD-UT-332"), Trait("Id", "BC-UT-522"), Trait("Id", "BC-UT-523"), Trait("Id", "SD-UT-335")]
    public async Task AKrCompany_DefaultsVat10AndItsBillingSetting_WarnsOnASharedBusinessNumber_AndHasNoExemptions()
    {
        await ExecuteAsync(
            """
            INSERT INTO bc_company (name, country_code, base_currency_code, default_lang_code, time_zone, billing_mode, created_by, updated_by)
            VALUES ('Quro', 'KR', 'KRW', 'ko', 'Asia/Seoul', 'SEPARATE', 't', 't');
            INSERT INTO bc_tax_code (tax_code, tax_kind, rate, created_by, updated_by)
            SELECT 'VAT10', 'VAT_STANDARD', 10, 't', 't' WHERE NOT EXISTS (SELECT 1 FROM bc_tax_code WHERE tax_code = 'VAT10');
            """);
        try
        {
            CreateCustomerRequest Kr(string name) => New(name) with
            {
                ZipCode = "04524",
                State = null,
                TaxCodeId = null,
                TaxPartyType = "BUSINESS",
                BusinessRegNo = "123-45-67891",
                CeoName = "Hong",
            };
            var first = await Service.CreateAsync(Kr(NewName()), "kim", None);
            var second = await Service.CreateAsync(Kr(NewName()), "kim", None);

            (first.TaxCode, first.BusinessRegNo, first.BillingFlow).Should().Be(("VAT10", "1234567891", "SHIP_THEN_BILL"));
            first.Warnings.Should().BeEmpty();
            second.Warnings.Should().ContainSingle(w => w.Code == Customer.SameBusinessRegNoWarning && Equals(w.Args[0], first.CName));
            (await CodeOfAsync(() => Service.CreateAsync(Kr(NewName()) with { ZipCode = "90001-1234" }, "kim", None)))
                .Should().Be(Customer.KrFieldCode);
            await FluentActions.Invoking(() => Service.GetTaxExemptionsAsync(first.Id, None)).Should().ThrowAsync<NotFoundException>();
        }
        finally
        {
            await ExecuteAsync("DELETE FROM bc_company");
        }
    }

    private static string NewName() => "CUST " + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

    private CreateCustomerRequest New(string name) => new()
    {
        CName = name,
        ShipName = "ship to",
        Phone1 = "555-0100",
        Rep = "KIM",
        ZipCode = "90001",
        Addr1 = "1 Main St",
        State = "CA",
        TaxCodeId = NoneTaxCodeId(),
    };

    private static UpdateCustomerRequest Change(CustomerDto c) => new()
    {
        ShipName = c.ShipName,
        Phone1 = c.Phone1,
        Rep = c.Rep,
        Terms = c.Terms,
        ZipCode = c.ZipCode,
        Addr1 = c.Addr1,
        State = c.State,
        ShipAddr1 = c.ShipAddr1,
        ShipZipCode = c.ShipZipCode,
        ShipState = c.ShipState,
        ValidFlag = c.ValidFlag,
        BillingFlow = c.BillingFlow,
        TaxCodeId = c.TaxCodeId,
        UpdatedAt = c.UpdatedAt,
    };

    private long NoneTaxCodeId()
    {
        using var connection = new NpgsqlConnection(_db.ConnectionString);
        return connection.QuerySingle<long>("SELECT id FROM bc_tax_code WHERE tax_code = 'NONE'");
    }

    private static async Task<string> CodeOfAsync(Func<Task> act) => (await act.Should().ThrowAsync<DomainException>()).Which.Code;

    private async Task InvoiceAsync(long customerId, decimal balance, string status) =>
        await ExecuteAsync(
            """
            INSERT INTO sd_sales_invoice (dn_no, customer_id, warehouse_id, business_place_id, currency_code, exchange_rate, bal_amt, status,
                                          created_by, updated_by)
            SELECT gen_random_uuid()::text, @customerId, id, business_place_id, 'USD', 1, @balance, @status, 't', 't'
            FROM bc_warehouse WHERE is_default
            """, new { customerId, balance, status });

    private async Task ExecuteAsync(string sql, object? parameters = null)
    {
        await using var connection = new NpgsqlConnection(_db.ConnectionString);
        await connection.ExecuteAsync(sql, parameters);
    }

    private sealed class FixedClock : IBusinessClock
    {
        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;

        public DateOnly Today() => CustomerServiceTests.Today;

        public DateTimeOffset ToCompanyTime(DateTimeOffset at) => at;

        public void Reset()
        {
        }
    }
}
