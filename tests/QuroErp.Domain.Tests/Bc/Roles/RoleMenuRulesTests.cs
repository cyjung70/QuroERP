using FluentAssertions;
using QuroErp.Domain.Bc.Roles;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Tests.Bc.Roles;

/// <summary>Role menu permission rules (BC-BR-212, 213, 216).</summary>
public class RoleMenuRulesTests
{
    [Fact, Trait("Id", "BC-UT-212")]
    public void AddingAnAssignedMenu_IsBcE212WithTheMenuId()
    {
        var act = () => RoleMenuRules.EnsureNoDuplicates(new HashSet<long> { 1, 2 }, [3, 2]);

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(RoleMenuRules.DuplicateMenuCode);
        error.Args.Should().Equal(2L);
    }

    [Fact, Trait("Id", "BC-UT-212")]
    public void AddingTheSameMenuTwice_IsBcE212()
    {
        var act = () => RoleMenuRules.EnsureNoDuplicates(new HashSet<long>(), [5, 5]);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(RoleMenuRules.DuplicateMenuCode);
    }

    [Fact, Trait("Id", "BC-UT-212")]
    public void AddingNewMenus_IsAllowed() => RoleMenuRules.EnsureNoDuplicates(new HashSet<long> { 1 }, [2, 3]);

    [Theory, Trait("Id", "BC-UT-213")]
    [InlineData(true, "Y")]
    [InlineData(false, "N")]
    public void FullAccess_IsStoredAsYOrN(bool fullAccess, string flag)
    {
        RoleMenuRules.ToFlag(fullAccess).Should().Be(flag);
        RoleMenuRules.IsFullAccess(flag).Should().Be(fullAccess);
    }

    [Fact, Trait("Id", "BC-UT-213")]
    public void MissingFlag_IsNotFullAccess() => RoleMenuRules.IsFullAccess(null).Should().BeFalse();

    [Fact, Trait("Id", "BC-UT-216")]
    public void RemovingTheLastFullAccessRole_IsBcE216()
    {
        var act = () => RoleMenuRules.EnsureRoleMenuAccessKept(new HashSet<long> { 2 }, new HashSet<long>(), myRoleId: 1);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(RoleMenuRules.LastAdminCode);
    }

    [Fact, Trait("Id", "BC-UT-216")]
    public void RemovingItFromMyOwnRole_IsBcE216_EvenWhenAnotherRoleKeepsIt()
    {
        var act = () => RoleMenuRules.EnsureRoleMenuAccessKept(new HashSet<long> { 1, 2 }, new HashSet<long> { 2 }, myRoleId: 1);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(RoleMenuRules.LastAdminCode);
    }

    [Fact, Trait("Id", "BC-UT-216")]
    public void RemovingItFromAnotherRole_WhileMineKeepsIt_IsAllowed() =>
        RoleMenuRules.EnsureRoleMenuAccessKept(new HashSet<long> { 1, 2 }, new HashSet<long> { 1 }, myRoleId: 1);

    [Fact, Trait("Id", "BC-UT-216")]
    public void WhenNoRoleHadItYet_SavingIsAllowed() =>
        RoleMenuRules.EnsureRoleMenuAccessKept(new HashSet<long>(), new HashSet<long>(), myRoleId: 1);
}
