using FluentAssertions;
using QuroErp.Domain.Common.Auth;

namespace QuroERP.Database.Tests;

/// <summary>
/// 0701_seed_sys_menu_screens (TASK-CORE-059): on a new database with only Schema and Seed applied, the admin role has Full Access
/// through a menu in use to every screen path that guards an API, so admin gets no 403 on any change (app-shell.md 3.2).
/// The API tests check that every change has a policy on one of these paths.
/// </summary>
public partial class MigrationTests
{
    [DatabaseFact, Trait("Id", "CORE-059-IT-01"), Trait("Rule", "BC-BR-214")]
    public void Seed_AdminHasFullAccess_ToEveryScreenPathThatGuardsAnApi()
    {
        var granted = Query<string>("""
            SELECT DISTINCT m.program_id
            FROM sys_role r
            JOIN sys_role_menu rm ON rm.role_id = r.id
            JOIN sys_menu m ON m.id = rm.menu_id
            WHERE r.role_code = 'admin' AND r.use_yn AND m.valid_flag = 'Y' AND rm.full_access = 'Y' AND m.program_id IS NOT NULL
            """);

        granted.Should().Contain(ScreenPaths.All);
    }

    [DatabaseFact, Trait("Id", "CORE-059-IT-02")]
    public void Seed_ScreenMenus_SitUnderTheirGroups_AndOtherRolesGetNoAssignment()
    {
        var misplaced = Query<string>("""
            SELECT m.menu_code FROM sys_menu m JOIN sys_menu g ON g.id = m.parent_menu_id
            WHERE m.type = 'P' AND left(m.menu_code, 4) <> g.menu_code
            """);
        var others = Query<long>("""
            SELECT count(*) FROM sys_role_menu rm JOIN sys_role r ON r.id = rm.role_id WHERE r.role_code <> 'admin'
            """);

        misplaced.Should().BeEmpty();
        others.Single().Should().Be(0);
    }
}
