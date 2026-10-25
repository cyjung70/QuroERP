using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using QuroErp.Application.Common.Auth;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Application.Tests.Common.Auth;

public class ApiKeyAuthenticatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeApiClientRepository _clients = new();

    private ApiKeyAuthenticator Authenticator => new(_clients, new FakeUnitOfWorkFactory(), new FakeTimeProvider(Now));

    [Fact, Trait("Id", "CORE-003-T18")]
    public async Task AuthenticateAsync_KnownKey_ReturnsClient()
    {
        _clients.ClientsByKeyHash[ApiKeyHash.Compute("key-1")] = new ApiClient { ClientCode = "edi", RoleCode = "manager" };

        var client = await Authenticator.AuthenticateAsync("key-1", CancellationToken.None);

        client!.ClientCode.Should().Be("edi");
    }

    [Fact, Trait("Id", "CORE-003-T19")]
    public async Task AuthenticateAsync_RevokedKey_ReturnsNull()
    {
        _clients.ClientsByKeyHash[ApiKeyHash.Compute("key-1")] = new ApiClient { ClientCode = "edi", RevokedAt = Now.AddMinutes(-1) };

        (await Authenticator.AuthenticateAsync("key-1", CancellationToken.None)).Should().BeNull();
    }

    [Theory, Trait("Id", "CORE-003-T20")]
    [InlineData("unknown")]
    [InlineData("")]
    public async Task AuthenticateAsync_UnknownOrEmptyKey_ReturnsNull(string apiKey)
    {
        (await Authenticator.AuthenticateAsync(apiKey, CancellationToken.None)).Should().BeNull();
    }

    [Fact, Trait("Id", "CORE-003-T21")]
    public void Compute_SameKey_ReturnsLowercaseSha256Hex()
    {
        var hash = ApiKeyHash.Compute("key-1");

        hash.Should().HaveLength(64).And.MatchRegex("^[0-9a-f]+$").And.Be(ApiKeyHash.Compute("key-1"));
    }
}
