namespace QuroErp.Application.Common.Auth;

/// <summary>비밀번호 해시 (security.md "비밀번호": PasswordHasher, PBKDF2).</summary>
public interface IPasswordHashService
{
    string Hash(string password);

    bool Verify(string passwordHash, string password);
}
