using Dapper;
using FluentAssertions;
using Npgsql;
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Mm.Labels;
using QuroErp.Contracts.Mm.Labels;
using QuroErp.Domain.Common;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Mm.Labels;

/// <summary>Own schema for the scan tests: their items must not reach other test classes.</summary>
[CollectionDefinition(Name)]
public sealed class LabelScanSchemaDefinition : ICollectionFixture<AuthSchemaFixture>
{
    public const string Name = "LabelScanSchema";
}

/// <summary>LabelScanService with the Dapper repository on real PostgreSQL (TASK-CORE-071, MM-BR-923).</summary>
[Collection(LabelScanSchemaDefinition.Name)]
public class LabelScanServiceDbTests(AuthSchemaFixture db)
{
    private static readonly CancellationToken None = CancellationToken.None;

    private LabelScanService Service => new(db.Get<ILabelScanRepository>(), db.Get<IUnitOfWorkFactory>());

    [DatabaseFact, Trait("Id", "MM-UT-923"), Trait("Rule", "MM-BR-923")]
    public async Task PlainValue_IsFoundByBarcodeFirst_ThenByItemCode()
    {
        var key = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var cable = await ItemAsync($"C{key}", "Cable", $"88{key}");
        // An item whose code is the barcode of the cable: the barcode wins
        await ItemAsync($"88{key}", "Other", null);
        var bolt = await ItemAsync($"B{key}", "Bolt", null);

        var byBarcode = await Service.ReadAsync(new LabelScanQuery { Value = $"88{key}" }, None);
        var byCode = await Service.ReadAsync(new LabelScanQuery { Value = $"B{key}", Accept = [LabelKinds.Item] }, None);

        byBarcode.Should().BeEquivalentTo(new LabelScanDto("IT", [$"C{key}"], cable, $"C{key}", "Cable"));
        byCode.Should().BeEquivalentTo(new LabelScanDto("IT", [$"B{key}"], bolt, $"B{key}", "Bolt"));
    }

    [DatabaseFact, Trait("Id", "MM-UT-923"), Trait("Rule", "MM-BR-923")]
    public async Task LotLabel_ReadsItsItem_AnUnknownItemIsRejected()
    {
        var key = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var item = await ItemAsync($"L{key}", "Reel", null);

        var lot = await Service.ReadAsync(new LabelScanQuery { Value = $"LT|L{key}|261104-001" }, None);
        var unknown = () => Service.ReadAsync(new LabelScanQuery { Value = $"LT|X{key}|261104-001" }, None);

        lot.Should().BeEquivalentTo(new LabelScanDto("LT", [$"L{key}", "261104-001"], item, $"L{key}", "Reel"));
        (await unknown.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("MM-E-923");
    }

    private async Task<long> ItemAsync(string itemCd, string name, string? upcCode)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        return await connection.ExecuteScalarAsync<long>(
            """
            INSERT INTO bc_item (item_cd, item_name, type, upc_code, created_by, updated_by)
            VALUES (@ItemCd, @Name, 'INVENTORY', @UpcCode, 't', 't') RETURNING id
            """,
            new { ItemCd = itemCd, Name = name, UpcCode = upcCode });
    }
}
