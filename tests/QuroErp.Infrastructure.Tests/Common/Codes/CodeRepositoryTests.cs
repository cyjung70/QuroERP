using Dapper;
using FluentAssertions;
using QuroErp.Application.Common.Codes;
using QuroErp.Application.Common.Persistence;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Common.Codes;

/// <summary>Code repository integration tests (TASK-CORE-005, real PostgreSQL, seed 0500 VALID_FLAG = Y/N/S).</summary>
[Collection(AuthSchemaDefinition.Name)]
public class CodeRepositoryTests(AuthSchemaFixture db)
{
    [DatabaseFact, Trait("Id", "CORE-005-IT-01")]
    public async Task FindActiveMinorsAsync_SeededMajor_ReturnsMinorsInSortOrder()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);

        var minors = await db.Get<ICodeRepository>().FindActiveMinorsAsync("VALID_FLAG", uow.Transaction, CancellationToken.None);

        minors!.Select(m => (m.MinorCode, m.SortOrder)).Should().Equal(("Y", 10), ("N", 20), ("S", 30));
    }

    [DatabaseFact, Trait("Id", "CORE-005-IT-02")]
    public async Task FindActiveMinorsAsync_UnknownMajor_ReturnsNull()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);

        var minors = await db.Get<ICodeRepository>().FindActiveMinorsAsync("NO_SUCH_MAJOR", uow.Transaction, CancellationToken.None);

        minors.Should().BeNull();
    }

    [DatabaseFact, Trait("Id", "CORE-005-IT-03")]
    public async Task ExistsActiveAsync_SeededCodes_ReturnsTrueOnlyForExistingMinor()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);
        var codes = db.Get<ICodeRepository>();

        (await codes.ExistsActiveAsync("VALID_FLAG", "Y", uow.Transaction, CancellationToken.None)).Should().BeTrue();
        (await codes.ExistsActiveAsync("VALID_FLAG", "X", uow.Transaction, CancellationToken.None)).Should().BeFalse();
        (await codes.ExistsActiveAsync("NO_SUCH_MAJOR", "Y", uow.Transaction, CancellationToken.None)).Should().BeFalse();
    }

    [DatabaseFact, Trait("Id", "CORE-005-IT-04")]
    public async Task InactiveMinorOrMajor_IsExcludedAndInvalid()
    {
        // Changes are rolled back when the unit of work is disposed without commit
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);
        var connection = uow.Transaction.Connection!;
        var codes = db.Get<ICodeRepository>();
        await connection.ExecuteAsync(
            """
            UPDATE sys_code_minor SET use_yn = false
            WHERE minor_code = 'S' AND major_id = (SELECT id FROM sys_code_major WHERE major_code = 'VALID_FLAG')
            """,
            transaction: uow.Transaction);

        var minors = await codes.FindActiveMinorsAsync("VALID_FLAG", uow.Transaction, CancellationToken.None);
        var inactiveMinorValid = await codes.ExistsActiveAsync("VALID_FLAG", "S", uow.Transaction, CancellationToken.None);

        await connection.ExecuteAsync("UPDATE sys_code_major SET use_yn = false WHERE major_code = 'VALID_FLAG'", transaction: uow.Transaction);
        var inactiveMajorMinors = await codes.FindActiveMinorsAsync("VALID_FLAG", uow.Transaction, CancellationToken.None);
        var inactiveMajorValid = await codes.ExistsActiveAsync("VALID_FLAG", "Y", uow.Transaction, CancellationToken.None);

        minors!.Select(m => m.MinorCode).Should().Equal("Y", "N");
        inactiveMinorValid.Should().BeFalse();
        inactiveMajorMinors.Should().BeNull();
        inactiveMajorValid.Should().BeFalse();
    }
}
