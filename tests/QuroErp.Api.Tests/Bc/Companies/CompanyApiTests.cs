using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Contracts.Bc.Companies;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Domain.Common.Auth;
using static QuroErp.Api.Tests.ApiFactory.InMemoryCompanyRepository;

namespace QuroErp.Api.Tests.Bc.Companies;

/// <summary>Reads of /api/bc/company and /api/sys/auth-settings, and requests that change nothing (BC-UC-007).</summary>
public class CompanyApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact, Trait("Id", "BC-UT-311")]
    public async Task Company_ForAnySignedInCaller_ReturnsNameAndSettings()
    {
        var company = await factory.NonAdmin().GetFromJsonAsync<CompanyDto>(CompanyRoutes.Base);

        company!.Name.Should().Be("Quro");
        (company.CountryCode, company.BaseCurrencyCode, company.DefaultLangCode, company.BillingMode).Should().Be(("KR", "KRW", "ko", "WITH_SHIPMENT"));
        company.UpdatedAt.Should().Be(Created);
    }

    [Fact, Trait("Id", "BC-UT-305")]
    public async Task Company_WithoutSignIn_Returns401()
    {
        using var response = await factory.CreateClient().GetAsync(CompanyRoutes.Base);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory, Trait("Id", "BC-UT-301")]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Task CreatingOrDeletingTheCompany_Returns405(string method)
    {
        var client = await factory.AdminAsync();

        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), CompanyRoutes.Base));

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Fact, Trait("Id", "BC-UT-308")]
    public async Task ThereIsNoBulkDelete()
    {
        var client = await factory.AdminAsync();

        using var response = await client.PostAsync(CompanyRoutes.Base + "/truncate", null);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
    }

    [Fact, Trait("Id", "BC-UT-305")]
    public async Task Changes_WithoutFullAccess_Return403()
    {
        var client = factory.NonAdmin();
        var request = new UpdateCompanyRequest(
            "Other", null, null, null, null, null, "2208162517", null, null, null, null, "KR", "KRW", "ko", "WITH_SHIPMENT", Created);

        using var company = await client.PutAsJsonAsync(CompanyRoutes.Base, request);
        using var logo = await client.DeleteAsync(CompanyRoutes.Logo);
        using var settings = await client.PutAsJsonAsync(AuthSettingRoutes.Base, ToDto(new AuthSetting(), factory.AuthSettingUpdatedAt));

        company.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        logo.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        settings.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        factory.Companies.Company!.Name.Should().Be("Quro");
    }

    [Fact, Trait("Id", "BC-UT-309")]
    public async Task AuthSettings_ForAnySignedInCaller_ReturnTheValues()
    {
        var setting = await factory.NonAdmin().GetFromJsonAsync<AuthSettingDto>(AuthSettingRoutes.Base);

        setting!.MaxFailedAttempts.Should().Be(5);
        setting.PasswordResetMode.Should().Be("ADMIN_ONLY");
    }

    internal static AuthSettingDto ToDto(AuthSetting s, DateTimeOffset updatedAt) => new(
        s.MaxFailedAttempts, s.LockoutMinutes, s.ShowRemainingAttempts, s.AutoLoginEnabled, s.AutoLoginValidDays, s.SessionIdleMinutes,
        s.AllowConcurrentLogin, s.PasswordMinLength, s.PasswordRequiredCategories, s.PasswordExpireDays, s.PasswordHistoryCount,
        s.PasswordResetMode, updatedAt);
}

/// <summary>Changes to the company, logo and security settings; each test has its own factory.</summary>
public class CompanySaveApiTests
{
    private static readonly UpdateCompanyRequest Request = new(
        "Quro Korea", "Seoul", null, "Lee", "02-000-0000", null, "220-81-62517", "Kim", "info@quro.kr", "Service", "Software",
        "KR", "KRW", "ko", "SEPARATE", Created);

    [Fact, Trait("Id", "BC-UC-007-T01")]
    public async Task Update_SavesAndReturnsTheCompany()
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();

        using var response = await client.PutAsJsonAsync(CompanyRoutes.Base, Request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await response.Content.ReadFromJsonAsync<CompanyDto>();
        (saved!.Name, saved.RegisterNo, saved.BusinessItem, saved.BillingMode).Should().Be(("Quro Korea", "2208162517", "Software", "SEPARATE"));
        factory.Companies.UpdatedBy.Should().Be(ApiFactory.UserId);
    }

    [Fact, Trait("Id", "BC-UC-007-T02")]
    public async Task Update_WithoutAName_Returns422BcE302()
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();

        using var response = await client.PutAsJsonAsync(CompanyRoutes.Base, Request with { Name = "" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be("BC-E-302");
        problem.GetProperty("args")[0].GetString().Should().Be("Name");
    }

    [Fact, Trait("Id", "BC-UC-007-T03")]
    public async Task Update_WithAStaleUpdatedAt_Returns409()
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();

        using var response = await client.PutAsJsonAsync(CompanyRoutes.Base, Request with { UpdatedAt = Created.AddSeconds(-1) });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("Common-E-004");
    }

    [Fact, Trait("Id", "BC-UC-007-T04")]
    public async Task Update_WithACurrencyNotInUse_Returns422BcE312()
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();

        using var response = await client.PutAsJsonAsync(CompanyRoutes.Base, Request with { BaseCurrencyCode = "EUR" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("BC-E-312");
    }

    [Fact, Trait("Id", "BC-UT-313")]
    public async Task Update_ToAnUnsupportedCountry_Returns400BcE313()
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();

        using var response = await client.PutAsJsonAsync(CompanyRoutes.Base, Request with { CountryCode = "JP" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("BC-E-313");
    }

    [Fact, Trait("Id", "BC-UT-312")]
    public async Task Update_ChangingTheCurrencyAfterTrading_Returns409BcE312()
    {
        await using var factory = new ApiFactory();
        factory.Companies.HasTransactions = true;
        var client = await factory.AdminAsync();

        using var response = await client.PutAsJsonAsync(CompanyRoutes.Base, Request with { BaseCurrencyCode = "USD" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("BC-E-312");
    }

    [Fact, Trait("Id", "BC-UT-307")]
    public async Task Update_WithTheCloseMonth_Returns400_AndSavesNothing()
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();
        var body = new
        {
            Request.Name,
            Request.CountryCode,
            Request.BaseCurrencyCode,
            Request.DefaultLangCode,
            Request.RegisterNo,
            Request.UpdatedAt,
            CloseYearMonth = "202610",
        };

        using var response = await client.PutAsJsonAsync(CompanyRoutes.Base, body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        factory.Companies.Company!.Name.Should().Be("Quro");
    }

    [Fact, Trait("Id", "BC-UT-306")]
    public async Task DeleteLogo_Returns204_ThenTheLogoIsGone()
    {
        await using var factory = new ApiFactory();
        factory.Companies.Logo = new([0x89, 0x50], "image/png");
        var client = await factory.AdminAsync();

        using var delete = await client.DeleteAsync(CompanyRoutes.Logo);
        using var get = await client.GetAsync(CompanyRoutes.Logo);
        var company = await client.GetFromJsonAsync<CompanyDto>(CompanyRoutes.Base);

        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await get.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("BC-E-007");
        company!.HasLogo.Should().BeFalse();
    }

    [Fact, Trait("Id", "BC-UT-310")]
    public async Task AuthSettings_TurningAutoLoginOff_RevokesTokens()
    {
        await using var factory = new ApiFactory();
        factory.AuthSetting = new AuthSetting { AutoLoginEnabled = true };
        factory.AutoLoginTokens.Tokens["t1"] = new(1, 7, DateTimeOffset.UtcNow.AddDays(1), null, DateTimeOffset.UtcNow);
        var client = await factory.AdminAsync();

        using var response = await client.PutAsJsonAsync(
            AuthSettingRoutes.Base, CompanyApiTests.ToDto(new AuthSetting { AutoLoginEnabled = false }, factory.AuthSettingUpdatedAt));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<AuthSettingDto>())!.AutoLoginEnabled.Should().BeFalse();
        factory.AutoLoginTokens.Tokens["t1"].RevokedAt.Should().NotBeNull();
    }

    [Fact, Trait("Id", "BC-UT-309")]
    public async Task AuthSettings_OutOfRange_Returns422BcE309()
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();

        using var response = await client.PutAsJsonAsync(
            AuthSettingRoutes.Base, CompanyApiTests.ToDto(new AuthSetting { MaxFailedAttempts = 20 }, factory.AuthSettingUpdatedAt));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be("BC-E-309");
        problem.GetProperty("args")[0].GetString().Should().Be("MaxFailedAttempts");
    }
}
