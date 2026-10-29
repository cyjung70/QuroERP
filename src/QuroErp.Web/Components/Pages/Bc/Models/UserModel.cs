using System.Globalization;
using QuroErp.Contracts.Bc.Users;

namespace QuroErp.Web.Components.Pages.Bc.Models;

/// <summary>
/// Editable user on the user management screen (BC-UC-004). <see cref="UserDto"/> is immutable, so the form binds to this.
/// The password fields are used only for a new user (BC-BR-123); an existing user's password changes only by reset (BC-BR-124).
/// </summary>
public sealed class UserModel
{
    // New user default shown on the screen; the API applies the same default. There is no default end date (BC-BR-126)
    public const string DefaultValidFlag = "Y";

    public long Id { get; set; }
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string? ConfirmPassword { get; set; }

    /// <summary>Role id as the select value.</summary>
    public string? RoleId { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? LangCode { get; set; }
    public DateOnly? UserValidDt { get; set; }
    public string? ValidFlag { get; set; } = DefaultValidFlag;

    // Read only: set by sign-in and the account actions (BC-BR-128)
    public int FailedCount { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public string? LastLoginIp { get; set; }
    public bool MustChangePassword { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public bool PasswordsMatch => Password == ConfirmPassword;

    public static UserModel From(UserDto dto) => new()
    {
        Id = dto.Id,
        UserId = dto.UserId,
        UserName = dto.UserName,
        RoleId = dto.RoleId?.ToString(CultureInfo.InvariantCulture),
        Email = dto.Email,
        Phone = dto.Phone,
        LangCode = dto.LangCode,
        UserValidDt = dto.UserValidDt,
        ValidFlag = dto.ValidFlag,
        FailedCount = dto.FailedCount,
        LockedUntil = dto.LockedUntil,
        LastLoginAt = dto.LastLoginAt,
        LastLoginIp = dto.LastLoginIp,
        MustChangePassword = dto.MustChangePassword,
        UpdatedAt = dto.UpdatedAt,
    };

    public CreateUserRequest ToCreateRequest() =>
        new(UserId, Password, UserName, ParsedRoleId, Email, Phone, LangCode, UserValidDt, ValidFlag);

    public UpdateUserRequest ToUpdateRequest() =>
        new(UserName, ParsedRoleId, Email, Phone, LangCode, UserValidDt, ValidFlag, UpdatedAt);

    private long? ParsedRoleId =>
        long.TryParse(RoleId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;
}
