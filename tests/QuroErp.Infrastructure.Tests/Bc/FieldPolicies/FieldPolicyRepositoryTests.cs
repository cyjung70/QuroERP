using FluentAssertions;
using QuroErp.Application.Bc.FieldPolicies;
using QuroErp.Application.Common.Persistence;
using QuroErp.Domain.Bc.FieldPolicies;
using QuroErp.Infrastructure.Bc.FieldPolicies;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Bc.FieldPolicies;

/// <summary>Company overrides (bc_field_policy, BC-BR-431, BC-BR-433) on real PostgreSQL. Each test rolls back.</summary>
[Collection(AuthSchemaDefinition.Name)]
public class FieldPolicyRepositoryTests(AuthSchemaFixture db)
{
    private static readonly CancellationToken None = CancellationToken.None;

    [DatabaseFact, Trait("Id", "CORE-047-IT-01"), Trait("Rule", "BC-BR-431")]
    public async Task Upsert_InsertsThenReplaces_AndDeleteRemoves()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var policies = db.Get<IFieldPolicyRepository>();

        await policies.UpsertAsync("Sd.Customer.State", new FieldPolicyEntry("OPTIONAL", null, "Company.Label.Region"), "kim", uow.Transaction, None);
        await policies.UpsertAsync("Sd.Customer.Email", new FieldPolicyEntry("HIDDEN", null, null), "kim", uow.Transaction, None);
        await policies.UpsertAsync("Sd.Customer.State", new FieldPolicyEntry(null, "NY", null), "lee", uow.Transaction, None);

        var rows = await policies.FindAllAsync(uow.Transaction, None);
        rows.Select(r => (r.FieldKey, r.State, r.DefaultValue, r.LabelKey, r.UpdatedBy)).Should().Equal(
            ("Sd.Customer.Email", "HIDDEN", null, null, "kim"),
            ("Sd.Customer.State", null, "NY", null, "lee"));

        await policies.DeleteAsync(["Sd.Customer.Email"], uow.Transaction, None);
        await policies.DeleteAsync([], uow.Transaction, None);

        (await policies.FindAllAsync(uow.Transaction, None)).Should().ContainSingle().Which.FieldKey.Should().Be("Sd.Customer.State");
    }

    [Fact, Trait("Id", "CORE-047-IT-02")]
    public void Packs_TheBuiltInUsAndKrPacks_AreRegistered()
    {
        var packs = CountryPackReader.ReadEmbedded();

        packs.Select(p => p.CountryCode).Should().Equal("KR", "US");
        packs[0].Policies["Sd.Customer.State"].State.Should().Be("HIDDEN");
        packs[1].Defaults["Sd.Customer.Country"].Should().Be("US");
        packs.Should().OnlyContain(p => p.Labels.Count == 2 && p.Labels.ContainsKey("en") && p.Labels.ContainsKey("ko"));
    }
}
