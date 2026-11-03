using Dapper;
using FluentAssertions;
using Npgsql;
using QuroErp.Application.Bc.Companies;
using QuroErp.Application.Common.Persistence;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Bc.Companies;

/// <summary>Company logo columns (0101_bc_company_logo.sql) and repository, real PostgreSQL. Each test rolls back.</summary>
[Collection(AuthSchemaDefinition.Name)]
public class CompanyRepositoryTests(AuthSchemaFixture db)
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01];

    [DatabaseFact, Trait("Id", "BC-007-IT-01")]
    public async Task NoCompany_FindReturnsNullAndUpdateReturnsFalse()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);
        var companies = db.Get<ICompanyRepository>();

        var logo = await companies.FindLogoAsync(uow.Transaction, CancellationToken.None);
        var updated = await companies.UpdateLogoAsync(Png, "image/png", "kim", uow.Transaction, CancellationToken.None);

        logo.Should().BeNull();
        updated.Should().BeFalse();
    }

    [DatabaseFact, Trait("Id", "BC-007-IT-02")]
    public async Task CompanyWithoutLogo_FindReturnsNull_ThenUpdateStoresLogoOnLowestId()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);
        var companies = db.Get<ICompanyRepository>();
        var connection = uow.Transaction.Connection!;
        await connection.ExecuteAsync(
            """
            INSERT INTO bc_company (id, name, country_code, base_currency_code, default_lang_code, time_zone, created_by, updated_by)
            VALUES (1, 'Quro', 'US', 'USD', 'en', 'America/New_York', 'system', 'system'), (2, 'Other', 'US', 'USD', 'en', 'America/New_York', 'system', 'system')
            """,
            transaction: uow.Transaction);

        var before = await companies.FindLogoAsync(uow.Transaction, CancellationToken.None);
        var updated = await companies.UpdateLogoAsync(Png, "image/png", "kim", uow.Transaction, CancellationToken.None);
        var after = await companies.FindLogoAsync(uow.Transaction, CancellationToken.None);
        var other = await connection.ExecuteScalarAsync<bool>(
            "SELECT logo_image IS NULL FROM bc_company WHERE id = 2", transaction: uow.Transaction);
        var updatedBy = await connection.ExecuteScalarAsync<string>(
            "SELECT updated_by FROM bc_company WHERE id = 1", transaction: uow.Transaction);

        before.Should().BeNull();
        updated.Should().BeTrue();
        after!.LogoImage.Should().Equal(Png);
        after.LogoContentType.Should().Be("image/png");
        other.Should().BeTrue();
        updatedBy.Should().Be("kim");
    }

    [DatabaseTheory, Trait("Id", "BC-007-IT-03")]
    [InlineData("'\\x01'::bytea", "'image/gif'")]
    [InlineData("'\\x01'::bytea", "NULL")]
    [InlineData("NULL", "'image/png'")]
    public async Task LogoConstraints_RejectOtherTypesAndHalfSetPairs(string image, string contentType)
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);

        // Test values only (InlineData), not external input
        var act = () => uow.Transaction.Connection!.ExecuteAsync(
            $"INSERT INTO bc_company (name, country_code, base_currency_code, default_lang_code, time_zone, logo_image, logo_content_type, created_by, updated_by) VALUES ('Quro', 'US', 'USD', 'en', 'America/New_York', {image}, {contentType}, 's', 's')",
            transaction: uow.Transaction);

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }
}
