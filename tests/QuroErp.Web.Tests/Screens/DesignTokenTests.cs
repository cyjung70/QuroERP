using System.Globalization;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace QuroErp.Web.Tests.Screens;

/// <summary>Brand tokens in wwwroot/css/quro-tokens.css follow entry-frame.md 7 (the logo colour) and keep text readable (TASK-CORE-034).</summary>
public partial class DesignTokenTests
{
    private static readonly string[] OldBrandColors = ["#1d4e89", "#14396a", "#e2ebf7", "#78aaea", "#a5c7f3", "#1b2c44"];

    public static TheoryData<string, string, string, string> BrandValues => new()
    {
        { ":root {", "#0049a8", "#00367d", "#e0eaf8" },
        { ":root:not([data-theme=\"light\"]) {", "#6ea6f5", "#a3c6f8", "#14294a" },
        { ":root[data-theme=\"dark\"] {", "#6ea6f5", "#a3c6f8", "#14294a" },
    };

    [Theory]
    [MemberData(nameof(BrandValues))]
    public void BrandTokens_MatchTheLogoColour_InEveryTheme(string block, string brand, string strong, string tint)
    {
        var tokens = Tokens(block);

        tokens["brand"].Should().Be(brand);
        tokens["brand-strong"].Should().Be(strong);
        tokens["brand-tint"].Should().Be(tint);
    }

    [Theory]
    [InlineData(":root {")]
    [InlineData(":root[data-theme=\"dark\"] {")]
    public void BrandColours_KeepTextAtLeast4_5To1(string block)
    {
        var t = Tokens(block);

        // Save button, title bar, selected row text, focus ring against a selected row
        Contrast(t["on-brand"], t["brand"]).Should().BeGreaterThanOrEqualTo(4.5);
        Contrast(t["on-brand"], t["brand-strong"]).Should().BeGreaterThanOrEqualTo(4.5);
        Contrast(t["ink"], t["brand-tint"]).Should().BeGreaterThanOrEqualTo(4.5);
        Contrast(t["brand"], t["brand-tint"]).Should().BeGreaterThanOrEqualTo(4.5);
    }

    [Fact]
    public void ComponentStyles_DoNotHardCodeTheOldBrandColours()
    {
        var files = Directory.GetFiles(Path.Combine(RepositoryRoot(), "src", "QuroErp.Web"), "*.css", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        foreach (var file in files)
        {
            var css = File.ReadAllText(file).ToLowerInvariant();
            OldBrandColors.Where(css.Contains).Should().BeEmpty(file);
        }
    }

    [Fact, Trait("Id", "CORE-036-WT-02")]
    public void LoginStyles_UseTokensOnly_InLightAndDark()
    {
        var css = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "QuroErp.Web", "Components", "Pages", "Bc", "Login.razor.css"));

        HexColor().Matches(CssComment().Replace(css, "")).Select(m => m.Value).Should().BeEmpty();
        css.Should().Contain("var(--q-brand)").And.Contain("@media (max-width: 767.98px)").And.Contain(":root[data-theme=\"dark\"] .login-brand");
    }

    private static Dictionary<string, string> Tokens(string block)
    {
        var css = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "QuroErp.Web", "wwwroot", "css", "quro-tokens.css"));
        var start = css.IndexOf(block, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"quro-tokens.css needs a {block} block");
        var body = css[start..css.IndexOf('}', start)];
        return TokenLine().Matches(body).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.ToLowerInvariant());
    }

    // WCAG 2.x contrast ratio of two #rrggbb colours
    private static double Contrast(string a, string b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(string hex)
    {
        double Channel(int i)
        {
            var c = int.Parse(hex.AsSpan(1 + (i * 2), 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return (0.2126 * Channel(0)) + (0.7152 * Channel(1)) + (0.0722 * Channel(2));
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "QuroErp.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("QuroErp.slnx was not found.");
    }

    [GeneratedRegex(@"--q-([a-z0-9-]+):\s*(#[0-9a-fA-F]{6})\s*;")]
    private static partial Regex TokenLine();

    [GeneratedRegex(@"#[0-9a-fA-F]{3,8}\b|\brgba?\(|\bwhite\b|\bblack\b")]
    private static partial Regex HexColor();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex CssComment();
}
