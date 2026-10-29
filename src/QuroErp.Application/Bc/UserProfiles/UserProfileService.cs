using QuroErp.Application.Common;
using QuroErp.Application.Common.Auth;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Bc.UserProfiles;
using QuroErp.Domain.Bc.UserProfiles;
using QuroErp.Domain.Common;

namespace QuroErp.Application.Bc.UserProfiles;

/// <summary>My info (BC-UC-003). Every method works on the signed-in user the caller passes in, never on an ID from the request (BC-BR-101).</summary>
public sealed class UserProfileService(
    IUserProfileRepository profiles, IAppUserRepository users, IAutoLoginTokenRepository autoLoginTokens, IUnitOfWorkFactory unitOfWork,
    TimeProvider clock)
{
    /// <summary>The user row is gone. Arg: id.</summary>
    public const string NotFoundCode = "Common-E-006";

    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task<UserProfileDto> GetAsync(long appUserId, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var row = await profiles.FindAsync(appUserId, forUpdate: false, uow.Transaction, cancellationToken)
            ?? throw new NotFoundException(NotFoundCode, appUserId);
        await uow.CommitAsync(cancellationToken);
        return ToDto(row);
    }

    /// <exception cref="DomainException">BC-E-102 with the property name, BC-E-405 for a language not in use.</exception>
    /// <exception cref="ConcurrencyException">updatedAt differs from the stored row (BC-BR-105).</exception>
    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task<UserProfileDto> UpdateAsync(
        long appUserId, UpdateUserProfileRequest request, string updatedBy, CancellationToken cancellationToken)
    {
        var fields = UserProfile.Normalize(
            new UserProfileFields(request.UserName, request.Email, request.Phone, request.LangCode, request.DefaultWarehouseId));

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var row = await profiles.FindAsync(appUserId, forUpdate: true, uow.Transaction, cancellationToken)
            ?? throw new NotFoundException(NotFoundCode, appUserId);
        if (row.UpdatedAt != request.UpdatedAt)
        {
            throw new ConcurrencyException();
        }
        if (fields.LangCode is { } lang && !await profiles.IsLanguageInUseAsync(lang, uow.Transaction, cancellationToken))
        {
            throw new DomainException(UserProfile.LanguageNotInUseCode, lang);
        }
        if (fields.DefaultWarehouseId is { } warehouseId && !await profiles.IsWarehouseInUseAsync(warehouseId, uow.Transaction, cancellationToken))
        {
            throw new DomainException(UserProfile.InvalidFieldCode, nameof(UserProfileFields.DefaultWarehouseId));
        }

        if (!await profiles.UpdateAsync(appUserId, fields, request.UpdatedAt, updatedBy, uow.Transaction, cancellationToken))
        {
            throw new ConcurrencyException();
        }
        var saved = await profiles.FindAsync(appUserId, forUpdate: false, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return ToDto(saved!);
    }

    /// <summary>
    /// "Log out on all devices": revokes every auto-login token of the user (BC-BR-106, BC-BR-013).
    /// The token version stays, so this session and its refresh keep working.
    /// </summary>
    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task RevokeTokensAsync(long appUserId, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        // Lock the user row before the tokens, as every token path does
        _ = await users.LockByIdAsync(appUserId, uow.Transaction, cancellationToken) ?? throw new NotFoundException(NotFoundCode, appUserId);
        await autoLoginTokens.RevokeAllAsync(appUserId, clock.GetUtcNow(), uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
    }

    private static UserProfileDto ToDto(UserProfileRow r) => new(
        r.UserId, r.UserName, r.Email, r.Phone, r.LangCode, r.DefaultWarehouseId, r.DefaultWarehouseCode, r.DefaultWarehouseName, r.UpdatedAt);
}
