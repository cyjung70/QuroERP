using FluentAssertions;
using QuroErp.Domain.Bc.UserProfiles;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Tests.Bc.UserProfiles;

/// <summary>My info field rules (BC-BR-102).</summary>
public class UserProfileTests
{
    private static readonly UserProfileFields Valid = new("Kim", "kim@example.com", "010-1234-5678", "ko", 3);

    [Fact, Trait("Id", "BC-UT-102")]
    public void Normalize_TrimsText_LowercasesLanguage_AndTurnsBlankIntoNull()
    {
        var fields = UserProfile.Normalize(new UserProfileFields("  Kim ", " ", "", " EN ", null));

        fields.Should().Be(new UserProfileFields("Kim", null, null, "en", null));
    }

    [Theory, Trait("Id", "BC-UT-102")]
    [InlineData(null)]
    [InlineData("  ")]
    public void MissingName_IsBcE102(string? name)
    {
        FieldOf(Valid with { UserName = name }).Should().Be(nameof(UserProfileFields.UserName));
    }

    [Fact, Trait("Id", "BC-UT-102")]
    public void Lengths_AllowTheMaximum_AndRejectOneMore()
    {
        var email30 = new string('a', 18) + "@example.com";

        UserProfile.Normalize(Valid with { UserName = new string('a', 40), Phone = new string('1', 18), Email = email30 })
            .Email.Should().HaveLength(30);
        FieldOf(Valid with { UserName = new string('a', 41) }).Should().Be(nameof(UserProfileFields.UserName));
        FieldOf(Valid with { Email = "a" + email30 }).Should().Be(nameof(UserProfileFields.Email));
        FieldOf(Valid with { Phone = new string('1', 19) }).Should().Be(nameof(UserProfileFields.Phone));
    }

    [Theory, Trait("Id", "BC-UT-102")]
    [InlineData("kim")]
    [InlineData("kim@example")]
    [InlineData("kim@@example.com")]
    [InlineData("kim lee@example.com")]
    [InlineData("@example.com")]
    [InlineData("kim@.com")]
    public void BadEmailFormat_IsBcE102(string email)
    {
        FieldOf(Valid with { Email = email }).Should().Be(nameof(UserProfileFields.Email));
    }

    private static object FieldOf(UserProfileFields fields)
    {
        var act = () => UserProfile.Normalize(fields);
        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(UserProfile.InvalidFieldCode);
        return error.Args.Single();
    }
}
