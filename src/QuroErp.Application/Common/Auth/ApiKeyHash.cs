using System.Security.Cryptography;
using System.Text;

namespace QuroErp.Application.Common.Auth;

/// <summary>
/// API 키 해시. 키는 발급할 때 만든 긴 난수라서 솔트 없이 SHA-256으로 찾는다 (sys_api_client.key_hash).
/// </summary>
public static class ApiKeyHash
{
    public static string Compute(string apiKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));
}
