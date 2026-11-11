using FluentAssertions;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Warehouses;

namespace QuroErp.Domain.Tests.Mm.Warehouses;

/// <summary>Users cannot pick the consignment warehouse (MM-BR-710).</summary>
public class WarehouseRulesTests
{
    private static readonly ConsignmentWarehouse Consignment = new(9, "CONSIGN");

    [Fact, Trait("Id", "MM-UT-710"), Trait("Rule", "MM-BR-710")]
    public void TheConsignmentWarehouse_IsMmE710_WithItsCode()
    {
        var act = () => WarehouseRules.EnsureUserSelectable([1, null, 9], Consignment);

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(WarehouseRules.ConsignmentNotSelectableCode);
        error.Args.Should().Equal("CONSIGN");
    }

    [Fact, Trait("Id", "MM-UT-710"), Trait("Rule", "MM-BR-710")]
    public void OtherWarehouses_EmptyValues_OrNoConsignmentWarehouse_Pass()
    {
        WarehouseRules.EnsureUserSelectable([1, 2, null], Consignment);
        WarehouseRules.EnsureUserSelectable([9], null);
    }
}
