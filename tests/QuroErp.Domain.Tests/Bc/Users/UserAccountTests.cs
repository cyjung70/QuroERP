using FluentAssertions;
using QuroErp.Domain.Bc.Users;
using QuroErp.Domain.Common;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Domain.Tests.Bc.Users;

/// <summary>User management rules (BC-BR-121, 122, 125, 126, 131).</summary>
public class UserAccountTests
{
    private static UserAccountFields Fields(
        string? name = "Kim", long? roleId = 1, string? email = null, string? phone = null, string? lang = null,
        DateOnly? validDt = null, string validFlag = "Y") =>
        new(name, roleId, email, phone, lang, validDt, validFlag);

    [Theory, Trait("Id", "BC-UT-121")]
    [InlineData("  Kim.Lee ", "kim.lee")]
    [InlineData("ADMIN", "admin")]
    public void NormalizeUserId_TrimsAndLowercases(string typed, string expected) =>
        UserAccount.NormalizeUserId(typed).Should().Be(expected);

    [Theory, Trait("Id", "BC-UT-121")]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("a12345678901234567890123456789012345678901234567890")]
    public void NormalizeUserId_EmptyOrLongerThan50_IsBcE122(string? typed)
    {
        var act = () => UserAccount.NormalizeUserId(typed);

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(UserAccount.InvalidFieldCode);
        error.Args.Should().Equal(UserAccount.UserIdField);
    }

    [Fact, Trait("Id", "BC-UT-121")]
    public void NormalizeUserId_Exactly50_IsAllowed() =>
        UserAccount.NormalizeUserId(new string('a', AppUser.MaxUserIdLength)).Should().HaveLength(50);

    [Theory, Trait("Id", "BC-UT-122")]
    [InlineData(" ", 1L, null, "UserName")]
    [InlineData("Kim", null, null, "RoleId")]
    [InlineData("Kim", 1L, "not-an-email", "Email")]
    public void Normalize_MissingNameOrRoleOrBadEmail_IsBcE122(string name, long? roleId, string? email, string field)
    {
        var act = () => UserAccount.Normalize(Fields(name, roleId, email));

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(UserAccount.InvalidFieldCode);
        error.Args.Should().Equal(field);
    }

    [Fact, Trait("Id", "BC-UT-122")]
    public void Normalize_TrimsText_BlankEmailIsAllowed_AndBlankValidFlagIsInUse()
    {
        var fields = UserAccount.Normalize(Fields(" Kim ", email: " ", phone: " 010 ", lang: "EN", validFlag: " "));

        fields.Should().Be(new UserAccountFields("Kim", 1, null, "010", "en", null, AppUser.ValidFlagUsed));
    }

    [Fact, Trait("Id", "BC-UT-126")]
    public void NoEndDate_MeansTheAccountNeverExpires()
    {
        var fields = UserAccount.Normalize(Fields(validDt: null));
        var user = new AppUser { ValidFlag = fields.ValidFlag, UserValidDt = fields.UserValidDt };

        user.CanLogin(new DateOnly(2099, 1, 1)).Should().BeTrue();
        new AppUser { ValidFlag = "Y", UserValidDt = new DateOnly(2026, 10, 28) }.CanLogin(new DateOnly(2026, 10, 29)).Should().BeFalse();
    }

    [Theory, Trait("Id", "BC-UT-131")]
    [InlineData(2L, "Y")]
    [InlineData(1L, "N")]
    public void SelfChange_OfRoleOrUse_IsBcE131(long roleId, string validFlag)
    {
        var act = () => UserAccount.EnsureSelfChangeAllowed(isSelf: true, currentRoleId: 1, Fields(roleId: roleId, validFlag: validFlag));

        act.Should().Throw<DomainException>().Which.Code.Should().Be(UserAccount.SelfChangeCode);
    }

    [Fact, Trait("Id", "BC-UT-131")]
    public void SelfChange_OfOtherFields_AndChangesOfOthers_AreAllowed()
    {
        UserAccount.EnsureSelfChangeAllowed(isSelf: true, currentRoleId: 1, Fields(name: "New Name"));
        UserAccount.EnsureSelfChangeAllowed(isSelf: false, currentRoleId: 1, Fields(roleId: 2, validFlag: "N"));
    }

    [Theory, Trait("Id", "BC-UT-125")]
    [InlineData("Y", "N", true)]
    [InlineData("Y", "S", true)]
    [InlineData("Y", "Y", false)]
    [InlineData("N", "N", false)]
    [InlineData("N", "Y", false)]
    public void StopsUse_OnlyWhenLeavingInUse(string current, string next, bool expected) =>
        UserAccount.StopsUse(current, Fields(validFlag: next)).Should().Be(expected);
}
