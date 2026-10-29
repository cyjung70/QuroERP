using System.Data;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using QuroErp.Application.Bc.UserProfiles;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Auth;
using QuroErp.Application.Tests.Common.Auth;
using QuroErp.Contracts.Bc.UserProfiles;
using QuroErp.Domain.Bc.UserProfiles;
using QuroErp.Domain.Common;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Application.Tests.Bc.UserProfiles;

/// <summary>My info service (BC-UC-003, BC-BR-101, 102, 105, 106, 403).</summary>
public class UserProfileServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Loaded = Now.AddHours(-1);

    private readonly FakeUserProfileRepository _profiles = new();
    private readonly FakeAppUserRepository _users = new();
    private readonly FakeAutoLoginTokenRepository _tokens = new();
    private readonly FakeUnitOfWorkFactory _unitOfWork = new();

    public UserProfileServiceTests()
    {
        _users.Users.Add(new AppUser { Id = 7, UserId = "kim" });
        _users.Users.Add(new AppUser { Id = 8, UserId = "lee" });
        _profiles.Rows[7] = new UserProfileRow(7, "kim", "Kim", null, null, "ko", null, "W01", "Main", Loaded);
        _profiles.Rows[8] = new UserProfileRow(8, "lee", "Lee", null, null, "en", null, "W01", "Main", Loaded);
    }

    private UserProfileService Service => new(_profiles, _users, _tokens, _unitOfWork, new FakeTimeProvider(Now));

    private static UpdateUserProfileRequest Request(string? lang = "en", long? warehouseId = 2, DateTimeOffset? updatedAt = null) =>
        new(" Kim Min ", "kim@example.com", "010-1234-5678", lang, warehouseId, updatedAt ?? Loaded);

    [Fact, Trait("Id", "BC-UT-101")]
    public async Task Get_ReturnsOnlyTheGivenUser()
    {
        var profile = await Service.GetAsync(7, CancellationToken.None);

        profile.Should().Be(new UserProfileDto("kim", "Kim", null, null, "ko", null, "W01", "Main", Loaded));
    }

    [Fact, Trait("Id", "BC-UC-003-T01")]
    public async Task Update_SavesNormalizedFields_OnlyForTheGivenUser()
    {
        var saved = await Service.UpdateAsync(7, Request(), "kim", CancellationToken.None);

        saved.Should().BeEquivalentTo(new { UserId = "kim", UserName = "Kim Min", Email = "kim@example.com", LangCode = "en", DefaultWarehouseId = 2L });
        _profiles.Rows[8].UserName.Should().Be("Lee");
        _profiles.UpdatedBy.Should().Equal("kim");
        _unitOfWork.Commits.Should().Be(1);
    }

    [Fact, Trait("Id", "BC-UC-003-T02")]
    public async Task Update_WithoutName_IsBcE102_AndSavesNothing()
    {
        var act = () => Service.UpdateAsync(7, Request() with { UserName = " " }, "kim", CancellationToken.None);

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be(UserProfile.InvalidFieldCode);
        error.Args.Should().Equal(nameof(UserProfileFields.UserName));
        _profiles.UpdatedBy.Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UT-105")]
    public async Task Update_WithAnOlderUpdatedAt_IsConcurrencyConflict()
    {
        var act = () => Service.UpdateAsync(7, Request(updatedAt: Loaded.AddSeconds(-1)), "kim", CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyException>();
        _profiles.UpdatedBy.Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UT-105")]
    public async Task Update_LosingTheRaceToAnotherSave_IsConcurrencyConflict()
    {
        _profiles.FailNextUpdate = true;

        var act = () => Service.UpdateAsync(7, Request(), "kim", CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyException>();
    }

    [Fact, Trait("Id", "BC-UT-403")]
    public async Task Update_WithALanguageNotInUse_IsBcE403()
    {
        var act = () => Service.UpdateAsync(7, Request(lang: "sw"), "kim", CancellationToken.None);

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be(UserProfile.LanguageNotInUseCode);
        error.Args.Should().Equal("sw");
    }

    [Fact, Trait("Id", "BC-UC-003-T04")]
    public async Task Update_WithAWarehouseNotInUse_IsBcE102()
    {
        var act = () => Service.UpdateAsync(7, Request(warehouseId: 9), "kim", CancellationToken.None);

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be(UserProfile.InvalidFieldCode);
        error.Args.Should().Equal(nameof(UserProfileFields.DefaultWarehouseId));
    }

    [Fact, Trait("Id", "BC-UT-102")]
    public async Task Update_WithEmptyLanguageAndWarehouse_SkipsTheChecks_AndUsesTheCompanyDefault()
    {
        var saved = await Service.UpdateAsync(7, Request(lang: null, warehouseId: null), "kim", CancellationToken.None);

        saved.LangCode.Should().BeNull();
        saved.DefaultWarehouseId.Should().BeNull();
    }

    [Fact, Trait("Id", "BC-UT-106")]
    public async Task RevokeTokens_RevokesEveryTokenOfTheUserOnly_AndKeepsTheTokenVersion()
    {
        _tokens.Tokens["a"] = new AutoLoginTokenRow(1, 7, Now.AddDays(10), null, Now.AddDays(-1));
        _tokens.Tokens["b"] = new AutoLoginTokenRow(2, 7, Now.AddDays(10), null, Now.AddDays(-2));
        _tokens.Tokens["c"] = new AutoLoginTokenRow(3, 8, Now.AddDays(10), null, Now.AddDays(-1));

        await Service.RevokeTokensAsync(7, CancellationToken.None);

        _tokens.Tokens["a"].RevokedAt.Should().Be(Now);
        _tokens.Tokens["b"].RevokedAt.Should().Be(Now);
        _tokens.Tokens["c"].RevokedAt.Should().BeNull();
        _users.Users.Single(u => u.Id == 7).TokenVersion.Should().Be(0);
        _unitOfWork.Commits.Should().Be(1);
    }

    [Fact, Trait("Id", "BC-UT-101")]
    public async Task UnknownUser_IsNotFound()
    {
        var act = () => Service.GetAsync(99, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private sealed class FakeUserProfileRepository : IUserProfileRepository
    {
        public Dictionary<long, UserProfileRow> Rows { get; } = [];

        public List<string> UpdatedBy { get; } = [];

        public bool FailNextUpdate { get; set; }

        public Task<UserProfileRow?> FindAsync(long appUserId, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(Rows.GetValueOrDefault(appUserId));

        public Task<bool> IsLanguageInUseAsync(string langCode, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(langCode is "ko" or "en");

        public Task<bool> IsWarehouseInUseAsync(long warehouseId, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(warehouseId is 1 or 2);

        public Task<bool> UpdateAsync(
            long appUserId, UserProfileFields fields, DateTimeOffset expectedUpdatedAt, string updatedBy,
            IDbTransaction transaction, CancellationToken cancellationToken)
        {
            if (FailNextUpdate)
            {
                FailNextUpdate = false;
                return Task.FromResult(false);
            }
            UpdatedBy.Add(updatedBy);
            Rows[appUserId] = Rows[appUserId] with
            {
                UserName = fields.UserName, Email = fields.Email, Phone = fields.Phone, LangCode = fields.LangCode,
                DefaultWarehouseId = fields.DefaultWarehouseId, UpdatedAt = expectedUpdatedAt.AddSeconds(1),
            };
            return Task.FromResult(true);
        }
    }
}
