using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Application.Common.Auth;

/// <summary>Security settings on the company screen (BC-UC-007, BC-BR-309, BC-BR-310, login.md "설정").</summary>
public sealed class AuthSettingService(
    IAuthSettingRepository settings, IAutoLoginTokenRepository autoLoginTokens, IUnitOfWorkFactory unitOfWork, TimeProvider clock)
{
    /// <summary>The settings row is made by the schema script.</summary>
    public const string SettingNotFoundCode = "Common-E-006";

    /// <exception cref="NotFoundException">Common-E-006 when the row is missing.</exception>
    public async Task<AuthSettingDto> GetAsync(CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var row = await settings.FindForEditAsync(false, uow.Transaction, cancellationToken) ?? throw new NotFoundException(SettingNotFoundCode);
        await uow.CommitAsync(cancellationToken);
        return ToDto(row);
    }

    /// <summary>
    /// Saves the settings and returns them as stored. Password policy changes apply from each user's next password change;
    /// stored passwords are not checked again (BC-BR-310).
    /// </summary>
    /// <exception cref="Domain.Common.DomainException">422 BC-E-309 with the property name.</exception>
    /// <exception cref="ConcurrencyException">409 when the settings changed after they were read.</exception>
    public async Task<AuthSettingDto> UpdateAsync(AuthSettingDto request, string updatedBy, CancellationToken cancellationToken)
    {
        var next = AuthSettingRules.Normalize(new AuthSetting
        {
            MaxFailedAttempts = request.MaxFailedAttempts,
            LockoutMinutes = request.LockoutMinutes,
            ShowRemainingAttempts = request.ShowRemainingAttempts,
            AutoLoginEnabled = request.AutoLoginEnabled,
            AutoLoginValidDays = request.AutoLoginValidDays,
            SessionIdleMinutes = request.SessionIdleMinutes,
            AllowConcurrentLogin = request.AllowConcurrentLogin,
            PasswordMinLength = request.PasswordMinLength,
            PasswordRequiredCategories = request.PasswordRequiredCategories,
            PasswordExpireDays = request.PasswordExpireDays,
            PasswordHistoryCount = request.PasswordHistoryCount,
            PasswordResetMode = request.PasswordResetMode,
        });

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var current = await settings.FindForEditAsync(true, uow.Transaction, cancellationToken) ?? throw new NotFoundException(SettingNotFoundCode);
        if (current.UpdatedAt != request.UpdatedAt
            || !await settings.UpdateAsync(next, request.UpdatedAt, updatedBy, uow.Transaction, cancellationToken))
        {
            throw new ConcurrencyException();
        }
        if (AuthSettingRules.RevokesAutoLoginTokens(current.Setting, next))
        {
            await autoLoginTokens.RevokeEveryUserAsync(clock.GetUtcNow(), uow.Transaction, cancellationToken);
        }
        var saved = (await settings.FindForEditAsync(false, uow.Transaction, cancellationToken))!;
        await uow.CommitAsync(cancellationToken);
        return ToDto(saved);
    }

    private static AuthSettingDto ToDto(AuthSettingRow row)
    {
        var s = row.Setting;
        return new AuthSettingDto(
            s.MaxFailedAttempts, s.LockoutMinutes, s.ShowRemainingAttempts, s.AutoLoginEnabled, s.AutoLoginValidDays, s.SessionIdleMinutes,
            s.AllowConcurrentLogin, s.PasswordMinLength, s.PasswordRequiredCategories, s.PasswordExpireDays, s.PasswordHistoryCount,
            s.PasswordResetMode, row.UpdatedAt);
    }
}
