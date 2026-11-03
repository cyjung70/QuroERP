using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Bc.Companies;
using QuroErp.Domain.Bc.Companies;
using QuroErp.Domain.Common;

namespace QuroErp.Application.Bc.Companies;

/// <summary>Company information (BC-UC-007, BC-BR-301~316). The company is one row made at installation; it is only read and changed.</summary>
public sealed class CompanyService(
    ICompanyRepository companies, ILocalizationRepository localization, IUnitOfWorkFactory unitOfWork, IBusinessClock clock)
{
    /// <exception cref="NotFoundException">BC-E-006 when there is no company.</exception>
    public async Task<CompanyDto> GetAsync(CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var company = await companies.FindAsync(false, uow.Transaction, cancellationToken)
            ?? throw new NotFoundException(CompanyLogoService.CompanyNotFoundCode);
        await uow.CommitAsync(cancellationToken);
        return company;
    }

    /// <summary>Saves the company and returns it as stored.</summary>
    /// <exception cref="BadRequestException">400 BC-E-313 for a country other than US or KR, BC-E-318 for an unknown time zone.</exception>
    /// <exception cref="DomainException">422 BC-E-302, 303, 312, 314, 316.</exception>
    /// <exception cref="ConflictException">409 BC-E-312 or 313 once trading started, BC-E-316 with unbilled shipments.</exception>
    /// <exception cref="ConcurrencyException">409 when the company changed after it was read (BC-BR-304).</exception>
    public async Task<CompanyDto> UpdateAsync(UpdateCompanyRequest request, string updatedBy, CancellationToken cancellationToken)
    {
        var fields = CompanyRules.Normalize(new CompanyFields(
            request.Name, request.Billto, request.Shipto, request.Contact, request.TelNo, request.FaxNo, request.Represent, request.Email,
            request.CountryCode, request.BaseCurrencyCode, request.DefaultLangCode, request.TimeZone, request.BillingMode));
        if (fields.CountryCode is not { } country || !CompanyRules.Countries.Contains(country))
        {
            throw new BadRequestException(CompanyRules.CountryCode, fields.CountryCode ?? "");
        }
        if (!CompanyRules.IsKnownTimeZone(fields.TimeZone))
        {
            throw new BadRequestException(CompanyRules.TimeZoneCode, fields.TimeZone ?? "");
        }

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var current = await companies.FindAsync(true, uow.Transaction, cancellationToken)
            ?? throw new NotFoundException(CompanyLogoService.CompanyNotFoundCode);
        if (current.UpdatedAt != request.UpdatedAt)
        {
            throw new ConcurrencyException();
        }

        var currencies = await localization.FindCurrenciesAsync(uow.Transaction, cancellationToken);
        if (!currencies.Any(c => c.UseYn && c.CurrencyCode == fields.BaseCurrencyCode))
        {
            throw new DomainException(CompanyRules.BaseCurrencyCode, fields.BaseCurrencyCode ?? "");
        }
        var languages = await localization.FindLanguagesAsync(uow.Transaction, cancellationToken);
        if (!languages.Any(l => l.UseYn && l.LangCode == fields.DefaultLangCode))
        {
            throw new DomainException(CompanyRules.DefaultLanguageCode, fields.DefaultLangCode ?? "");
        }

        // BC-BR-312, BC-BR-313: country and base currency are fixed once any transaction exists
        var countryChanged = country != current.CountryCode;
        if ((countryChanged || fields.BaseCurrencyCode != current.BaseCurrencyCode)
            && await companies.HasTransactionsAsync(uow.Transaction, cancellationToken))
        {
            throw countryChanged
                ? new ConflictException(CompanyRules.CountryCode, country)
                : new ConflictException(CompanyRules.BaseCurrencyCode, fields.BaseCurrencyCode!);
        }
        if (CompanyRules.NeedsAllShipmentsBilled(current.BillingMode, fields.BillingMode!)
            && await companies.HasUnbilledShipmentsAsync(uow.Transaction, cancellationToken))
        {
            throw new ConflictException(CompanyRules.BillingModeCode, fields.BillingMode!);
        }

        if (!await companies.UpdateAsync(fields, request.UpdatedAt, updatedBy, uow.Transaction, cancellationToken))
        {
            throw new ConcurrencyException();
        }
        var saved = (await companies.FindAsync(false, uow.Transaction, cancellationToken))!;
        await uow.CommitAsync(cancellationToken);
        // The business date follows the new time zone from the next call (BC-BR-318)
        clock.Reset();
        return saved;
    }
}
