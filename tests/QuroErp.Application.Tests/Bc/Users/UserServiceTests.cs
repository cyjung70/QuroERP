using System.Data;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using QuroErp.Application.Bc.UserProfiles;
using QuroErp.Application.Bc.Users;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Auth;
using QuroErp.Application.Common.Codes;
using QuroErp.Application.Tests.Common.Auth;
using QuroErp.Contracts.Bc.Users;
using QuroErp.Domain.Bc.UserProfiles;
using QuroErp.Domain.Bc.Users;
using QuroErp.Domain.Common;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Application.Tests.Bc.Users;

/// <summary>User management service (BC-UC-004, BC-BR-121~132, BC-BR-403).</summary>
public class UserServiceTests
{
    private const long AdminId = 7;
    private const long TargetId = 8;
    private const string NewPassword = "Reset-Pass-9";
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Loaded = Now.AddHours(-1);
    private static readonly AuthClient Client = new("10.0.0.9", "test-agent");

    private readonly FakeUserRepository _users = new();
    private readonly FakeCodeValidator _codes = new();
    private readonly FakeAutoLoginTokenRepository _tokens = new();
    private readonly FakeUserLoginLogRepository _logs = new();
    private readonly FakePasswordHistoryRepository _history = new();
    private readonly FakeAuthSettingRepository _settings = new();
    private readonly FakeUnitOfWorkFactory _unitOfWork = new();

    public UserServiceTests()
    {
        _users.Rows[AdminId] = Row(AdminId, "kim", roleId: 1);
        _users.Rows[TargetId] = Row(TargetId, "lee", roleId: 2);
        _users.Hashes[TargetId] = "hash:Old-Pass-1";
    }

    private UserService Service => new(
        _users, new FakeLanguages(), _codes, _tokens, _logs, _history, _settings, new FakePasswordHashService(), _unitOfWork,
        new FakeTimeProvider(Now));

    private static UserRow Row(long id, string userId, long roleId, string validFlag = "Y", DateTimeOffset? lockedUntil = null) =>
        new(id, userId, userId.ToUpperInvariant(), roleId, "role" + roleId, null, null, "ko", null, validFlag, false,
            lockedUntil is null ? 0 : 5, lockedUntil, null, null, Loaded);

    private static CreateUserRequest Create(string? userId = " New.User ", string? password = "Initial-Pass-1") =>
        new(userId, password, " New User ", 2, "new@example.com", "010", "en", null, null);

    private static UpdateUserRequest Update(long roleId = 2, string validFlag = "Y", DateTimeOffset? updatedAt = null) =>
        new(" Lee Two ", roleId, null, null, "ko", new DateOnly(2027, 12, 31), validFlag, updatedAt ?? Loaded);

    private void AddToken(long appUserId, string hash) =>
        _tokens.Tokens[hash] = new AutoLoginTokenRow(_tokens.Tokens.Count + 1, appUserId, Now.AddDays(14), null, Now.AddDays(-1));

    [Fact, Trait("Id", "BC-UC-004-T01")]
    public async Task Create_SavesALowercaseUserWhoMustChangeThePassword()
    {
        var saved = await Service.CreateAsync(Create(), "kim", CancellationToken.None);

        saved.Should().BeEquivalentTo(new { UserId = "new.user", UserName = "New User", RoleId = 2L, LangCode = "en", ValidFlag = "Y", MustChangePassword = true });
        _users.Inserted.Should().ContainSingle().Which.Should().Be(("new.user", "hash:Initial-Pass-1", "kim"));
        _unitOfWork.Commits.Should().Be(1);
    }

    [Fact, Trait("Id", "BC-UC-004-T02")]
    public async Task Create_WithoutName_IsBcE122_AndSavesNothing()
    {
        var act = () => Service.CreateAsync(Create() with { UserName = " " }, "kim", CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal("UserName");
        _users.Inserted.Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UT-121")]
    public async Task Create_UsedUserId_IsBcE121()
    {
        var act = () => Service.CreateAsync(Create(userId: " LEE "), "kim", CancellationToken.None);

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be(UserAccount.DuplicateUserIdCode);
        error.Args.Should().Equal("lee");
    }

    [Fact, Trait("Id", "BC-UT-122")]
    public async Task Create_RoleNotInUse_IsBcE122OnRoleId()
    {
        _users.RolesInUse.Remove(2);

        var act = () => Service.CreateAsync(Create(), "kim", CancellationToken.None);

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be(UserAccount.InvalidFieldCode);
        error.Args.Should().Equal(UserAccount.RoleIdField);
    }

    [Theory, Trait("Id", "BC-UT-123")]
    [InlineData(null, AuthCodes.Required)]
    [InlineData("short", AuthCodes.PasswordLength)]
    [InlineData("alllowercase", AuthCodes.PasswordCategories)]
    [InlineData("New.User", AuthCodes.PasswordSameAsUserId)]
    public async Task Create_InitialPasswordFollowsThePasswordRules(string? password, string code)
    {
        var act = () => Service.CreateAsync(Create(password: password), "kim", CancellationToken.None);

        (await act.Should().ThrowAsync<AuthException>()).Which.Code.Should().Be(code);
        _users.Inserted.Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UC-004-T04")]
    public async Task Save_UnknownValidFlag_IsCommonE002()
    {
        var act = () => Service.UpdateAsync(TargetId, Update(validFlag: "X"), AdminId, "kim", CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(FakeCodeValidator.InvalidCode);
    }

    [Fact, Trait("Id", "BC-UT-403")]
    public async Task Save_LanguageNotInUse_IsBcE405()
    {
        var act = () => Service.CreateAsync(Create() with { LangCode = "fr" }, "kim", CancellationToken.None);

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be(UserProfile.LanguageNotInUseCode);
        error.Args.Should().Equal("fr");
    }

    [Fact, Trait("Id", "BC-UT-124")]
    public async Task Update_SavesFields_NeverTheUserIdOrPassword()
    {
        var saved = await Service.UpdateAsync(TargetId, Update(), AdminId, "kim", CancellationToken.None);

        saved.Should().BeEquivalentTo(new { UserId = "lee", UserName = "Lee Two", UserValidDt = new DateOnly(2027, 12, 31) });
        _users.Hashes[TargetId].Should().Be("hash:Old-Pass-1");
        _users.TokenVersionRaised.Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UC-004-T03")]
    public async Task Update_WithAnOlderUpdatedAt_IsConcurrencyConflict()
    {
        var act = () => Service.UpdateAsync(TargetId, Update(updatedAt: Loaded.AddSeconds(-1)), AdminId, "kim", CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyException>();
        _users.Rows[TargetId].UserName.Should().Be("LEE");
    }

    [Fact, Trait("Id", "BC-UT-125")]
    public async Task Update_StopUsing_RevokesAllTokensAndRaisesTheTokenVersion()
    {
        AddToken(TargetId, "t1");
        AddToken(AdminId, "t2");

        await Service.UpdateAsync(TargetId, Update(validFlag: "N"), AdminId, "kim", CancellationToken.None);

        _tokens.Tokens["t1"].RevokedAt.Should().Be(Now);
        _tokens.Tokens["t2"].RevokedAt.Should().BeNull();
        _users.TokenVersionRaised.Should().Equal(TargetId);
    }

    [Theory, Trait("Id", "BC-UT-131")]
    [InlineData(2L, "Y")]
    [InlineData(1L, "N")]
    public async Task Update_OwnRoleOrUse_IsBcE131(long roleId, string validFlag)
    {
        var act = () => Service.UpdateAsync(AdminId, Update(roleId, validFlag), AdminId, "kim", CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(UserAccount.SelfChangeCode);
    }

    [Fact, Trait("Id", "BC-UT-127")]
    public async Task Create_HasNoPerUserPasswordExpiry()
    {
        var saved = await Service.CreateAsync(Create(), "kim", CancellationToken.None);

        typeof(UserDto).GetProperty("PasswordValidDt").Should().BeNull();
        typeof(CreateUserRequest).GetProperty("PasswordValidDt").Should().BeNull();
        saved.MustChangePassword.Should().BeTrue();
    }

    [Fact, Trait("Id", "BC-UT-128")]
    public async Task Unlock_LockedUser_ClearsTheLockAndLogsUnlock()
    {
        _users.Rows[TargetId] = Row(TargetId, "lee", 2, lockedUntil: Now.AddMinutes(30));

        await Service.UnlockAsync(TargetId, Client, CancellationToken.None);

        _users.Rows[TargetId].Should().BeEquivalentTo(new { FailedCount = 0, LockedUntil = (DateTimeOffset?)null });
        _logs.Logs.Should().ContainSingle().Which.Should().Be(
            new UserLoginLog(Now, TargetId, "lee", "10.0.0.9", true, LoginEvents.Unlock, null, "test-agent"));
    }

    [Theory, Trait("Id", "BC-UT-128")]
    [InlineData(null)]
    [InlineData(-1)]
    public async Task Unlock_NotLocked_ChangesNothing(int? lockMinutes)
    {
        _users.Rows[TargetId] = Row(TargetId, "lee", 2, lockedUntil: lockMinutes is { } m ? Now.AddMinutes(m) : null);

        await Service.UnlockAsync(TargetId, Client, CancellationToken.None);

        _users.Unlocked.Should().BeEmpty();
        _logs.Logs.Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UT-129")]
    public async Task ResetPassword_InOneTransaction_SetsMustChange_RevokesTokens_RaisesVersion_AndKeepsHistory()
    {
        AddToken(TargetId, "t1");

        await Service.ResetPasswordAsync(TargetId, new ResetPasswordRequest(NewPassword), "kim", CancellationToken.None);

        _users.Hashes[TargetId].Should().Be("hash:" + NewPassword);
        _users.Rows[TargetId].MustChangePassword.Should().BeTrue();
        _users.TokenVersionRaised.Should().Equal(TargetId);
        _tokens.Tokens["t1"].RevokedAt.Should().Be(Now);
        _history.Rows.Should().ContainSingle().Which.Should().Be((TargetId, "hash:Old-Pass-1", "kim"));
        _unitOfWork.Commits.Should().Be(1);
    }

    [Theory, Trait("Id", "BC-UT-129")]
    [InlineData(null, AuthCodes.Required)]
    [InlineData("Old-Pass-1", AuthCodes.PasswordReused)]
    [InlineData("short", AuthCodes.PasswordLength)]
    public async Task ResetPassword_BreakingTheRules_ChangesNothing(string? password, string code)
    {
        var act = () => Service.ResetPasswordAsync(TargetId, new ResetPasswordRequest(password), "kim", CancellationToken.None);

        (await act.Should().ThrowAsync<AuthException>()).Which.Code.Should().Be(code);
        _users.Hashes[TargetId].Should().Be("hash:Old-Pass-1");
        _history.Rows.Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UT-130")]
    public async Task RevokeTokens_RevokesAllOfTheUser_AndRaisesTheTokenVersion()
    {
        AddToken(TargetId, "t1");
        AddToken(TargetId, "t2");
        AddToken(AdminId, "t3");

        await Service.RevokeTokensAsync(TargetId, CancellationToken.None);

        _tokens.Tokens.Where(t => t.Value.AppUserId == TargetId).Should().OnlyContain(t => t.Value.RevokedAt == Now);
        _tokens.Tokens["t3"].RevokedAt.Should().BeNull();
        _users.TokenVersionRaised.Should().Equal(TargetId);
    }

    [Fact, Trait("Id", "BC-UT-132")]
    public void UserDto_HasNoPasswordHash() =>
        typeof(UserDto).GetProperties().Should().NotContain(p => p.Name.Contains("Password", StringComparison.Ordinal) && p.Name != "MustChangePassword");

    [Fact, Trait("Id", "BC-UC-004-T01")]
    public async Task Actions_OnAMissingUser_AreNotFound()
    {
        await FluentActions.Awaiting(() => Service.GetAsync(99, CancellationToken.None)).Should().ThrowAsync<NotFoundException>();
        await FluentActions.Awaiting(() => Service.UnlockAsync(99, Client, CancellationToken.None)).Should().ThrowAsync<NotFoundException>();
        await FluentActions.Awaiting(() => Service.RevokeTokensAsync(99, CancellationToken.None)).Should().ThrowAsync<NotFoundException>();
    }

    private sealed class FakeCodeValidator : ICodeValidator
    {
        public const string InvalidCode = "Common-E-002";

        public Task EnsureValidAsync(string majorCode, string? value, CancellationToken cancellationToken) =>
            majorCode == CodeMajors.ValidFlag && value is not ("Y" or "N" or "S")
                ? throw new DomainException(InvalidCode, majorCode, value ?? "")
                : Task.CompletedTask;
    }

    // Only the language check is used by the user service
    private sealed class FakeLanguages : IUserProfileRepository
    {
        public Task<UserProfileRow?> FindAsync(long appUserId, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> IsLanguageInUseAsync(string langCode, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(langCode is "ko" or "en");

        public Task<bool> IsWarehouseInUseAsync(long warehouseId, IDbTransaction transaction, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> UpdateAsync(
            long appUserId, UserProfileFields fields, DateTimeOffset expectedUpdatedAt, string updatedBy,
            IDbTransaction transaction, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        public Dictionary<long, UserRow> Rows { get; } = [];

        public Dictionary<long, string> Hashes { get; } = [];

        public HashSet<long> RolesInUse { get; } = [1, 2];

        public List<(string UserId, string Hash, string CreatedBy)> Inserted { get; } = [];

        public List<long> Unlocked { get; } = [];

        public List<long> TokenVersionRaised { get; } = [];

        public Task<(IReadOnlyList<UserRow> Users, int Total)> SearchAsync(UserSearch search, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<(IReadOnlyList<UserRow>, int)>((Rows.Values.OrderBy(r => r.UserId).ToList(), Rows.Count));

        public Task<UserRow?> FindAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(Rows.GetValueOrDefault(id));

        public Task<UserRow?> FindAdjacentAsync(long id, bool forward, IDbTransaction transaction, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string?> FindPasswordHashAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(Hashes.GetValueOrDefault(id));

        public Task<bool> UserIdExistsAsync(string userId, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(Rows.Values.Any(r => r.UserId == userId));

        public Task<bool> IsRoleInUseAsync(long roleId, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(RolesInUse.Contains(roleId));

        public Task<long> InsertAsync(
            string userId, string passwordHash, UserAccountFields fields, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            var id = Rows.Keys.Max() + 1;
            Inserted.Add((userId, passwordHash, createdBy));
            Hashes[id] = passwordHash;
            Rows[id] = new UserRow(id, userId, fields.UserName, fields.RoleId, "role" + fields.RoleId, fields.Email, fields.Phone, fields.LangCode,
                fields.UserValidDt, fields.ValidFlag, true, 0, null, null, null, Now);
            return Task.FromResult(id);
        }

        public Task<bool> UpdateAsync(
            long id, UserAccountFields fields, bool raiseTokenVersion, DateTimeOffset expectedUpdatedAt, string updatedBy,
            IDbTransaction transaction, CancellationToken cancellationToken)
        {
            if (Rows[id].UpdatedAt != expectedUpdatedAt)
            {
                return Task.FromResult(false);
            }
            Rows[id] = Rows[id] with
            {
                UserName = fields.UserName, RoleId = fields.RoleId, Email = fields.Email, Phone = fields.Phone, LangCode = fields.LangCode,
                UserValidDt = fields.UserValidDt, ValidFlag = fields.ValidFlag, UpdatedAt = Now,
            };
            if (raiseTokenVersion)
            {
                TokenVersionRaised.Add(id);
            }
            return Task.FromResult(true);
        }

        public Task UnlockAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Unlocked.Add(id);
            Rows[id] = Rows[id] with { FailedCount = 0, LockedUntil = null };
            return Task.CompletedTask;
        }

        public Task ResetPasswordAsync(
            long id, string passwordHash, DateTimeOffset now, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Hashes[id] = passwordHash;
            Rows[id] = Rows[id] with { MustChangePassword = true, UpdatedAt = now };
            TokenVersionRaised.Add(id);
            return Task.CompletedTask;
        }

        public Task RaiseTokenVersionAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            TokenVersionRaised.Add(id);
            return Task.CompletedTask;
        }
    }
}
