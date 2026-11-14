using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using QuroErp.Web.Common;

namespace QuroErp.Web.Tests.Screens;

/// <summary>Screen write access (security.md "화면 쓰기 권한", TASK-CORE-076).</summary>
public class ScreenAccessTests : BunitContext
{
    [Theory, Trait("Id", "BC-UT-214")]
    [InlineData("admin", true)]
    [InlineData("user", false)]
    public async Task CanWrite_IsTheAdminRole_ForNow(string role, bool expected)
    {
        AddAuthorization().SetAuthorized("kim").SetRoles(role);
        var access = new ScreenAccess(Services.GetRequiredService<AuthenticationStateProvider>());

        (await access.CanWriteAsync("/bc/item")).Should().Be(expected);
    }

    [Fact, Trait("Id", "BC-UT-214")]
    public async Task CanWrite_SignedOut_IsFalse_AndAScreenPathIsRequired()
    {
        AddAuthorization();
        var access = new ScreenAccess(Services.GetRequiredService<AuthenticationStateProvider>());

        (await access.CanWriteAsync("/bc/item")).Should().BeFalse();
        await access.Invoking(a => a.CanWriteAsync(" ")).Should().ThrowAsync<ArgumentException>();
    }

    // Screens decide write access through IScreenAccess only, never through roles
    [Fact]
    public void WebSource_HasNoRoleCheck()
    {
        var web = Path.Combine(RepoRoot(), "src", "QuroErp.Web");
        var hits = Directory.EnumerateFiles(web, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("IsInRole(", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(web, f));

        hits.Should().BeEmpty();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "QuroErp.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("QuroErp.slnx was not found.");
    }
}
