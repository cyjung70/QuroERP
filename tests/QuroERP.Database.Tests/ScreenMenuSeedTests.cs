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

    // 0728 (TASK-CORE-071): the PDA screens have menus of their own, granted like the PC screens (screens.md "PDA 화면")
    [DatabaseFact, Trait("Id", "CORE-071-IT-01"), Trait("Rule", "BC-BR-214")]
    public void Seed_PdaMenus_HaveTheirOwnPaths_WithAdminFullAccess()
    {
        var menus = Query<string>("""
            SELECT m.menu_code || ' ' || m.program_id
            FROM sys_menu m
            JOIN sys_role_menu rm ON rm.menu_id = m.id AND rm.full_access = 'Y'
            JOIN sys_role r ON r.id = rm.role_id AND r.role_code = 'admin'
            WHERE m.program_id LIKE '%/pda-%' AND m.valid_flag = 'Y'
            ORDER BY m.menu_code
            """);

        menus.Should().Equal("G2MM08 /mm/pda-receipt", "G2MM09 /mm/pda-move", "G3SD06 /sd/pda-issue");
    }

    // 0729 (TASK-SD-071): a database where G3SD06 held another program before 0728 gets the PDA Issue menu back
    [DatabaseFact, Trait("Id", "SD-071-IT-01"), Trait("Rule", "BC-BR-214")]
    public void Seed_PdaIssueFix_SetsG3SD06ToThePdaIssueScreen_WhenNoMenuOpensIt()
    {
        using var schema = new ScratchSchema();
        schema.ApplySchemaScriptsBefore("9999");
        schema.Execute("""
            INSERT INTO sys_role (role_code, created_by, updated_by) VALUES ('admin', 't', 't');
            INSERT INTO sys_menu (menu_code, name, type, module, valid_flag, created_by, updated_by) VALUES ('G3SD', 'Sales', 'M', 'sd', 'Y', 't', 't');
            INSERT INTO sys_menu (menu_code, name, type, program_id, module, parent_menu_id, valid_flag, created_by, updated_by)
            SELECT 'G3SD06', 'AR Receipt', 'P', '/sd/ar-receipt', 'sd', id, 'Y', 't', 't' FROM sys_menu WHERE menu_code = 'G3SD';
            """);
        schema.ApplyScript("Seed", "0728_seed_sys_menu_pda.sql");

        schema.ApplyScript("Seed", "0729_seed_sys_menu_pda_issue_fix.sql");
        schema.ApplyScript("Seed", "0729_seed_sys_menu_pda_issue_fix.sql");

        schema.Rows("""
            SELECT concat_ws(' ', m.menu_code, m.name, m.program_id, g.menu_code, rm.full_access)
            FROM sys_menu m JOIN sys_menu g ON g.id = m.parent_menu_id
            LEFT JOIN sys_role_menu rm ON rm.menu_id = m.id
            WHERE m.menu_code LIKE 'G3SD0%'
            """).Should().Equal("G3SD06 PDA Issue /sd/pda-issue G3SD Y");
    }
}
