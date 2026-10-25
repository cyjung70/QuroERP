using Microsoft.AspNetCore.Identity;
using QuroErp.Application.Common.Auth;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Infrastructure.Common.Auth;

/// <summary>PasswordHasher&lt;AppUser&gt;(PBKDF2, Identity V3 형식). QuroERP.Database의 관리자 생성과 같은 형식이다.</summary>
internal sealed class PasswordHashService : IPasswordHashService
{
    private static readonly AppUser HashUser = new();
    private readonly PasswordHasher<AppUser> _hasher = new();

    public string Hash(string password) => _hasher.HashPassword(HashUser, password);

    public bool Verify(string passwordHash, string password)
    {
        try
        {
            return _hasher.VerifyHashedPassword(HashUser, passwordHash, password) != PasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            // 해시 형식이 아닌 값(이관 전 평문 등)은 실패로 본다
            return false;
        }
    }
}
