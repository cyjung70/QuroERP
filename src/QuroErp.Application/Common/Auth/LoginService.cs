using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Application.Common.Auth;

/// <summary>로그인과 토큰 재발급 (BC-UC-001, security.md).</summary>
public sealed class LoginService(
    IAppUserRepository users,
    IUserLoginLogRepository loginLogs,
    IPasswordHashService passwords,
    IAccessTokenIssuer tokens,
    IUnitOfWorkFactory unitOfWork,
    TimeProvider clock)
{
    /// <summary>
    /// 사용자 ID·비밀번호를 확인하고 토큰을 발급한다. 성공·실패를 모두 로그인 이력에 남긴다.
    /// </summary>
    /// <returns>실패하면 null. 실패 이유(없는 ID, 틀린 비밀번호, 사용 중지)는 구분하지 않는다.</returns>
    public async Task<LoginResponse?> LoginAsync(LoginRequest request, string? ipAddr, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var today = Today();

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var user = await users.FindByUserIdAsync(request.UserId, uow.Transaction, cancellationToken);
        bool success;
        if (user is null)
        {
            // 없는 ID도 해시 계산 시간을 같게 해서 응답 시간으로 ID 존재를 알 수 없게 한다
            passwords.Hash(request.Password);
            success = false;
        }
        else
        {
            success = passwords.Verify(user.PasswordHash, request.Password) && user.CanLogin(today);
        }

        await loginLogs.InsertAsync(
            new UserLoginLog(now, user?.Id, request.UserId, ipAddr, success), uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);

        return success ? ToResponse(user!, now, today) : null;
    }

    /// <summary>로그인한 사용자의 토큰을 다시 발급한다. 그 사이 사용 중지된 사용자면 null.</summary>
    public async Task<LoginResponse?> RefreshAsync(long appUserId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var today = Today();

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var user = await users.FindByIdAsync(appUserId, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);

        return user is not null && user.CanLogin(today) ? ToResponse(user, now, today) : null;
    }

    // [추정] 업무 날짜는 서버 현지 날짜로 본다
    private DateOnly Today() => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    private LoginResponse ToResponse(AppUser user, DateTimeOffset now, DateOnly today)
    {
        var token = tokens.Issue(user, now);
        return new LoginResponse(
            token.Token, token.ExpiresAt, user.UserId, user.UserName, user.RoleCode, user.RequiresPasswordChange(today),
            user.LangCode);
    }
}
