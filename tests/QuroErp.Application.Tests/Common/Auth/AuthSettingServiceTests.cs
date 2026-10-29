using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Auth;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Domain.Common;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Application.Tests.Common.Auth;

/// <summary>Security settings (BC-UC-007, BC-BR-309, BC-BR-310).</summary>
public class AuthSettingServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 9, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly FakeAuthSettingRepository _settings = new() { Setting = new AuthSetting { AutoLoginEnabled = true } };
    private readonly FakeAutoLoginTokenRepository _tokens = new();
    private readonly FakeUnitOfWorkFactory _uow = new();

    private AuthSettingService Service => new(_settings, _tokens, _uow, new FakeTimeProvider(Now));

    [Fact, Trait("Id", "BC-UT-309")]
    public async Task Get_ReturnsTheSettingWithUpdatedAt()
    {
        var setting = await Service.GetAsync(None);

        setting.AutoLoginEnabled.Should().BeTrue();
        setting.MaxFailedAttempts.Should().Be(5);
        setting.UpdatedAt.Should().Be(_settings.UpdatedAt);
    }

    [Fact, Trait("Id", "BC-UT-309")]
    public async Task Update_SavesTheValues()
    {
        var current = await Service.GetAsync(None);
        var commits = _uow.Commits;

        var saved = await Service.UpdateAsync(current with { MaxFailedAttempts = 3, PasswordResetMode = "email" }, "kim", None);

        saved.MaxFailedAttempts.Should().Be(3);
        saved.PasswordResetMode.Should().Be("EMAIL");
        _uow.Commits.Should().Be(commits + 1);
    }

    [Fact, Trait("Id", "BC-UT-309")]
    public async Task Update_OutOfRange_IsBcE309_AndWritesNothing()
    {
        var current = await Service.GetAsync(None);

        var act = () => Service.UpdateAsync(current with { SessionIdleMinutes = 1 }, "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal("SessionIdleMinutes");
        _settings.Updates.Should().Be(0);
    }

    [Fact, Trait("Id", "BC-UT-310")]
    public async Task TurningAutoLoginOff_RevokesEveryUsersTokens()
    {
        await AddTokensAsync();
        var current = await Service.GetAsync(None);

        await Service.UpdateAsync(current with { AutoLoginEnabled = false }, "kim", None);

        _tokens.Tokens.Values.Should().OnlyContain(t => t.RevokedAt == Now);
    }

    [Fact, Trait("Id", "BC-UT-310")]
    public async Task OtherChanges_KeepTheTokens()
    {
        await AddTokensAsync();
        var current = await Service.GetAsync(None);

        await Service.UpdateAsync(current with { PasswordMinLength = 12 }, "kim", None);

        _tokens.Tokens.Values.Should().OnlyContain(t => t.RevokedAt == null);
    }

    [Fact, Trait("Id", "BC-UT-304")]
    public async Task Update_WithAStaleUpdatedAt_Is409()
    {
        var current = await Service.GetAsync(None);

        var act = () => Service.UpdateAsync(current with { UpdatedAt = current.UpdatedAt.AddSeconds(-1), AutoLoginEnabled = false }, "kim", None);

        await act.Should().ThrowAsync<ConcurrencyException>();
        _settings.Setting.AutoLoginEnabled.Should().BeTrue();
    }

    private async Task AddTokensAsync()
    {
        await using var uow = await ((IUnitOfWorkFactory)_uow).BeginAsync(None);
        await _tokens.InsertAsync(1, "a", Now.AddDays(1), null, Now.AddHours(-1), uow.Transaction, None);
        await _tokens.InsertAsync(2, "b", Now.AddDays(1), null, Now.AddHours(-1), uow.Transaction, None);
    }
}
