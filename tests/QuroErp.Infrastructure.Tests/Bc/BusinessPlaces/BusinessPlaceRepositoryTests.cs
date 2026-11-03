using FluentAssertions;
using QuroErp.Application.Bc.BusinessPlaces;
using QuroErp.Application.Common.Persistence;
using QuroErp.Domain.Bc.BusinessPlaces;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Bc.BusinessPlaces;

/// <summary>Business places (BC-BR-319, BC-BR-320) on real PostgreSQL. Each test rolls back.</summary>
[Collection(AuthSchemaDefinition.Name)]
public class BusinessPlaceRepositoryTests(AuthSchemaFixture db)
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static readonly BusinessPlaceFields Fields = new(
        "Quro Seoul", "2208162517", "0001", "Kim", "Service", "Software", "1 Sejong-daero", "3F", "Seoul", null, "04524", "02-000-0000", true);

    [DatabaseFact, Trait("Id", "CORE-046-IT-07"), Trait("Rule", "BC-BR-319")]
    public async Task List_HasTheOneDefaultPlace_WhichNewDocumentsGet()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var places = db.Get<IBusinessPlaceRepository>();

        var list = await places.ListAsync(uow.Transaction, None);

        var main = list.Should().ContainSingle().Which;
        (main.BusinessPlaceCode, main.IsDefault, main.UseYn).Should().Be(("MAIN", true, true));
        (await places.FindDefaultIdAsync(uow.Transaction, None)).Should().Be(main.Id);
    }

    [DatabaseFact, Trait("Id", "CORE-046-IT-08"), Trait("Rule", "BC-BR-320")]
    public async Task Update_ChecksUpdatedAt_AndStoresTheSupplierValues()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var places = db.Get<IBusinessPlaceRepository>();
        var id = (await places.FindDefaultIdAsync(uow.Transaction, None))!.Value;
        var loaded = (await places.FindAsync(id, true, uow.Transaction, None))!;

        var stale = await places.UpdateAsync(id, Fields, loaded.UpdatedAt.AddSeconds(-1), "kim", uow.Transaction, None);
        var saved = await places.UpdateAsync(id, Fields, loaded.UpdatedAt, "kim", uow.Transaction, None);
        var after = (await places.FindAsync(id, false, uow.Transaction, None))!;

        stale.Should().BeFalse();
        saved.Should().BeTrue();
        after.Should().BeEquivalentTo(new
        {
            BusinessPlaceCode = "MAIN",
            BusinessPlaceName = "Quro Seoul",
            BusinessRegNo = "2208162517",
            SubBusinessNo = "0001",
            CeoName = "Kim",
            BusinessItem = "Software",
            Addr2 = "3F",
            ZipCode = "04524",
            UpdatedBy = "kim",
        });
        after.UpdatedAt.Should().BeOnOrAfter(loaded.UpdatedAt);
    }
}
