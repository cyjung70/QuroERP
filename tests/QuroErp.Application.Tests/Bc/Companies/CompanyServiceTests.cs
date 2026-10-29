using System.Data;
using FluentAssertions;
using QuroErp.Application.Bc.Companies;
using QuroErp.Application.Common;
using QuroErp.Application.Tests.Bc.Localization;
using QuroErp.Application.Tests.Common.Auth;
using QuroErp.Contracts.Bc.Companies;
using QuroErp.Domain.Bc.Companies;
using QuroErp.Domain.Common;

namespace QuroErp.Application.Tests.Bc.Companies;

/// <summary>Company information (BC-UC-007, BC-BR-301~316) with fake storage.</summary>
public class CompanyServiceTests
{
    private static readonly DateTimeOffset Read = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken None = CancellationToken.None;

    private static readonly UpdateCompanyRequest Request = new(
        "Quro", "1 Main St", null, null, "010-0000-0000", null, "220-81-62517", "Kim", "info@quro.kr", "Service", "Software",
        "KR", "KRW", "ko", "WITH_SHIPMENT", Read);

    private readonly FakeCompanies _companies = new();
    private readonly FakeLocalizationRepository _localization = new();
    private readonly FakeUnitOfWorkFactory _uow = new();
    private readonly CompanyService _service;

    public CompanyServiceTests() => _service = new CompanyService(_companies, _localization, _uow);

    [Fact, Trait("Id", "BC-UT-301")]
    public async Task Get_WithoutACompany_IsNotFoundBcE006()
    {
        _companies.Company = null;

        (await FluentActions.Awaiting(() => _service.GetAsync(None)).Should().ThrowAsync<NotFoundException>())
            .Which.Code.Should().Be(CompanyLogoService.CompanyNotFoundCode);
    }

    [Fact, Trait("Id", "BC-UC-007-T01")]
    public async Task Update_SavesNormalizedValues_AndCommits()
    {
        var saved = await _service.UpdateAsync(Request with { Name = " Quro Korea " }, "kim", None);

        saved.Name.Should().Be("Quro Korea");
        saved.RegisterNo.Should().Be("2208162517");
        saved.BusinessType.Should().Be("Service");
        saved.UpdatedAt.Should().BeAfter(Read);
        _uow.Commits.Should().Be(1);
    }

    [Fact, Trait("Id", "BC-UC-007-T02")]
    public async Task Update_WithoutAName_IsBcE302_AndWritesNothing()
    {
        var act = () => _service.UpdateAsync(Request with { Name = " " }, "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(CompanyRules.InvalidNameCode);
        _companies.Writes.Should().Be(0);
        _uow.Commits.Should().Be(0);
    }

    [Fact, Trait("Id", "BC-UC-007-T03")]
    public async Task Update_WithAStaleUpdatedAt_Is409()
    {
        var act = () => _service.UpdateAsync(Request with { UpdatedAt = Read.AddSeconds(-1) }, "kim", None);

        await act.Should().ThrowAsync<ConcurrencyException>();
        _companies.Writes.Should().Be(0);
    }

    [Theory, Trait("Id", "BC-UC-007-T04")]
    [InlineData("EUR", "ko", "BC-E-312")]
    [InlineData("KRW", "sw", "BC-E-314")]
    [InlineData("KRW", "fr", "BC-E-314")]
    public async Task Update_WithACurrencyOrLanguageNotInUse_Is422(string currency, string language, string code)
    {
        _localization.Languages[2] = new("sw", 3, false);

        var act = () => _service.UpdateAsync(Request with { BaseCurrencyCode = currency, DefaultLangCode = language }, "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(code);
    }

    [Theory, Trait("Id", "BC-UT-313")]
    [InlineData("JP")]
    [InlineData(null)]
    public async Task Update_WithAnUnsupportedCountry_Is400BcE313(string? country)
    {
        var act = () => _service.UpdateAsync(Request with { CountryCode = country }, "kim", None);

        (await act.Should().ThrowAsync<BadRequestException>()).Which.Code.Should().Be(CompanyRules.CountryCode);
    }

    [Fact, Trait("Id", "BC-UT-313")]
    public async Task Update_ChangingTheCountryAfterTrading_Is409BcE313()
    {
        _companies.HasTransactions = true;

        var act = () => _service.UpdateAsync(
            Request with { CountryCode = "US", BaseCurrencyCode = "USD", DefaultLangCode = "en", RegisterNo = null }, "kim", None);

        (await act.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be(CompanyRules.CountryCode);
    }

    [Fact, Trait("Id", "BC-UT-312")]
    public async Task Update_ChangingTheBaseCurrencyAfterTrading_Is409BcE312()
    {
        _companies.HasTransactions = true;

        var act = () => _service.UpdateAsync(Request with { BaseCurrencyCode = "USD" }, "kim", None);

        var error = (await act.Should().ThrowAsync<ConflictException>()).Which;
        error.Code.Should().Be(CompanyRules.BaseCurrencyCode);
        error.Args.Should().Equal("USD");
    }

    [Fact, Trait("Id", "BC-UT-312")]
    public async Task Update_ChangingCountryAndCurrencyBeforeTrading_IsAllowed()
    {
        var saved = await _service.UpdateAsync(
            Request with { CountryCode = "us", BaseCurrencyCode = "usd", DefaultLangCode = "en", RegisterNo = null }, "kim", None);

        (saved.CountryCode, saved.BaseCurrencyCode, saved.DefaultLangCode).Should().Be(("US", "USD", "en"));
    }

    [Fact, Trait("Id", "BC-UT-312")]
    public async Task Update_KeepingCountryAndCurrency_DoesNotLookForTrading()
    {
        _companies.HasTransactions = true;

        await _service.UpdateAsync(Request with { DefaultLangCode = "en" }, "kim", None);

        _companies.TradingChecks.Should().Be(0);
    }

    [Fact, Trait("Id", "BC-UT-316")]
    public async Task Update_BackToWithShipment_WithUnbilledShipments_Is409BcE316()
    {
        _companies.Company = _companies.Company! with { BillingMode = CompanyRules.Separate };
        _companies.HasUnbilledShipments = true;

        var act = () => _service.UpdateAsync(Request, "kim", None);

        (await act.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be(CompanyRules.BillingModeCode);
    }

    [Fact, Trait("Id", "BC-UT-316")]
    public async Task Update_ToSeparate_WithUnbilledShipments_IsAllowed()
    {
        _companies.HasUnbilledShipments = true;

        var saved = await _service.UpdateAsync(Request with { BillingMode = "SEPARATE" }, "kim", None);

        saved.BillingMode.Should().Be("SEPARATE");
    }

    private sealed class FakeCompanies : ICompanyRepository
    {
        public CompanyDto? Company { get; set; } = new(
            "Quro", null, null, null, null, null, "2208162517", null, null, null, null, "KR", "KRW", "ko", "WITH_SHIPMENT", false, Read);

        public bool HasTransactions { get; set; }

        public bool HasUnbilledShipments { get; set; }

        public int Writes { get; private set; }

        public int TradingChecks { get; private set; }

        public Task<CompanyDto?> FindAsync(bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken) => Task.FromResult(Company);

        public Task<bool> UpdateAsync(
            CompanyFields fields, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Writes++;
            if (Company is null || Company.UpdatedAt != expectedUpdatedAt)
            {
                return Task.FromResult(false);
            }
            Company = new CompanyDto(
                fields.Name!, fields.Billto, fields.Shipto, fields.Contact, fields.TelNo, fields.FaxNo, fields.RegisterNo, fields.Represent,
                fields.Email, fields.BusinessType, fields.BusinessItem, fields.CountryCode!, fields.BaseCurrencyCode!, fields.DefaultLangCode!,
                fields.BillingMode!, Company.HasLogo, Company.UpdatedAt.AddMinutes(1));
            return Task.FromResult(true);
        }

        public Task<bool> HasTransactionsAsync(IDbTransaction transaction, CancellationToken cancellationToken)
        {
            TradingChecks++;
            return Task.FromResult(HasTransactions);
        }

        public Task<bool> HasUnbilledShipmentsAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(HasUnbilledShipments);

        public Task<CompanyLogoRow?> FindLogoAsync(IDbTransaction transaction, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> UpdateLogoAsync(
            byte[] image, string contentType, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> DeleteLogoAsync(string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
