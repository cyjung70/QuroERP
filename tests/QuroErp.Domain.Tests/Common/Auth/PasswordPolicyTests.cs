using FluentAssertions;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Domain.Tests.Common.Auth;

/// <summary>New password rules with the default policy: min 8, 3 kinds, history 3 (BC-BR-053~056, 059).</summary>
public class PasswordPolicyTests
{
    private static readonly AuthSetting Setting = new();

    private static PasswordViolation? Check(string password, bool reused = false, string userId = "kim") =>
        PasswordPolicy.FirstViolation(password, userId, Setting, () => reused);

    [Theory, Trait("Id", "BC-UT-053")]
    [InlineData("Ab1!xyz")]
    [InlineData("")]
    public void TooShort_IsAuth011WithMinimum(string password)
    {
        Check(password).Should().BeEquivalentTo(new PasswordViolation(AuthCodes.PasswordLength, 8));
    }

    [Fact, Trait("Id", "BC-UT-053")]
    public void LongerThan128_IsAuth011_And128IsAllowed()
    {
        Check("Aa1" + new string('x', 126))!.Code.Should().Be(AuthCodes.PasswordLength);
        Check("Aa1" + new string('x', 125)).Should().BeNull();
    }

    [Theory, Trait("Id", "BC-UT-054")]
    [InlineData("abcdefgh1", 2)]
    [InlineData("ABCDEFGH", 1)]
    [InlineData("abcd 1234", 3)]
    [InlineData("Abcdefg1!", 4)]
    [InlineData("한글비밀번호abc1", 3)]
    public void Categories_CountsUpperLowerDigitAndOtherAsSymbol(string password, int expected)
    {
        PasswordPolicy.Categories(password).Should().Be(expected);
    }

    [Fact, Trait("Id", "BC-UT-054")]
    public void TwoKinds_IsAuth012WithRequiredKinds()
    {
        Check("abcdefgh1").Should().BeEquivalentTo(new PasswordViolation(AuthCodes.PasswordCategories, 3));
    }

    [Fact, Trait("Id", "BC-UT-055")]
    public void Reused_IsAuth013()
    {
        Check("Abcdefg1", reused: true)!.Code.Should().Be(AuthCodes.PasswordReused);
    }

    [Theory, Trait("Id", "BC-UT-056")]
    [InlineData("Kim.User1", "kim.user1")]
    [InlineData("KIM.USER1", "kim.user1")]
    public void SameAsUserIdIgnoringCase_IsAuth014(string password, string userId)
    {
        Check(password, userId: userId)!.Code.Should().Be(AuthCodes.PasswordSameAsUserId);
    }

    [Fact, Trait("Id", "BC-UT-059")]
    public void SeveralViolations_ReturnTheFirstInOrder_AndSkipTheHistoryCheckBeforeIt()
    {
        var historyChecked = false;

        var violation = PasswordPolicy.FirstViolation("kim", "kim", Setting, () => historyChecked = true);

        violation!.Code.Should().Be(AuthCodes.PasswordLength);
        historyChecked.Should().BeFalse();
        Check("kimuser12", reused: true, userId: "kimuser12")!.Code.Should().Be(AuthCodes.PasswordCategories);
        Check("Kim.User1", reused: true, userId: "kim.user1")!.Code.Should().Be(AuthCodes.PasswordReused);
    }

    [Fact, Trait("Id", "BC-UT-053")]
    public void ValidPassword_HasNoViolation()
    {
        Check("Spring-2026").Should().BeNull();
    }
}
