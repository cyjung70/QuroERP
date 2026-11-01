using System.Data;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Bc.Localization;
using QuroErp.Contracts.Common;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Common;

namespace QuroErp.Application.Bc.Localization;

/// <summary>Exchange rate management (BC-UC-024, SD-BR-905, SD-BR-906, SD-BR-907, SD-BR-908, BC-BR-412).</summary>
public sealed class ExchangeRateService(
    IExchangeRateRepository rates, ICurrencyRepository currencies, ILocalizationRepository localization, IBusinessClock clock,
    IUnitOfWorkFactory unitOfWork)
{
    /// <summary>Rates from..to of one currency or all (SD-BR-908); the period defaults to the year up to today in the company time zone.</summary>
    public async Task<IReadOnlyList<ExchangeRateDto>> SearchAsync(
        DateOnly? from, DateOnly? to, string? currencyCode, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var list = await ReadAsync(from, to, currencyCode, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return list;
    }

    /// <summary>
    /// Saves added, changed and deleted rows in one transaction (SD-BR-905). One failing row saves nothing; its date and currency
    /// are the message args. Returns the rates of the given period and currency after the save.
    /// </summary>
    /// <exception cref="DomainException">422 SD-E-907 (date, currency, rate) or SD-E-906 (date and currency already have a rate).</exception>
    /// <exception cref="ConcurrencyException">409 Common-E-004 when a row changed or was deleted after it was read.</exception>
    public async Task<IReadOnlyList<ExchangeRateDto>> SaveAsync(
        BatchRequest<ExchangeRateSaveRow, ExchangeRateKey> request, DateOnly? from, DateOnly? to, string? currencyCode, string updatedBy,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ExchangeRateSaveRow> added = request.Added ?? [];
        IReadOnlyList<ExchangeRateSaveRow> updated = request.Updated ?? [];
        IReadOnlyList<ExchangeRateKey> deleted = request.Deleted ?? [];

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        await rates.LockAsync(uow.Transaction, cancellationToken);

        // Rows the save names must still be as they were read; otherwise someone else saved first
        var named = updated.Select(u => (Id: u.Id ?? 0, u.UpdatedAt)).Concat(deleted.Select(d => (d.Id, UpdatedAt: (DateTimeOffset?)d.UpdatedAt))).ToList();
        var stored = (await rates.FindByIdsAsync([.. named.Select(n => n.Id).Distinct()], uow.Transaction, cancellationToken)).ToDictionary(r => r.Id);
        if (named.Any(n => !stored.TryGetValue(n.Id, out var row) || row.UpdatedAt != n.UpdatedAt))
        {
            throw new ConcurrencyException();
        }

        var inUse = (await currencies.FindAllAsync(forUpdate: false, uow.Transaction, cancellationToken))
            .Where(c => c.UseYn).Select(c => c.CurrencyCode).ToHashSet(StringComparer.Ordinal);
        var baseCurrency = (await localization.FindCompanyAsync(uow.Transaction, cancellationToken))?.BaseCurrencyCode;
        var newRows = added.Select(r => ExchangeRateRules.Normalize(r.RateDt, r.CurrencyCode, r.Rate, inUse, baseCurrency)).ToList();
        var changedRows = updated
            .Select(r => (Id: r.Id!.Value, Fields: ExchangeRateRules.Normalize(r.RateDt, r.CurrencyCode, r.Rate, inUse, baseCurrency)))
            .ToList();

        // SD-BR-906 over the rates the save leaves: the saved rows, plus stored rows on the same keys that the save does not touch
        var saved = newRows.Concat(changedRows.Select(r => r.Fields)).Select(f => (f.RateDt, f.CurrencyCode)).ToList();
        var touched = named.Select(n => n.Id).ToHashSet();
        var others = (await rates.FindByKeysAsync([.. saved.Distinct()], uow.Transaction, cancellationToken))
            .Where(r => !touched.Contains(r.Id))
            .Select(r => (r.RateDt, r.CurrencyCode));
        ExchangeRateRules.EnsureUnique(others.Concat(saved));

        await rates.DeleteAsync([.. deleted.Select(d => d.Id)], uow.Transaction, cancellationToken);
        foreach (var (id, fields) in changedRows)
        {
            if (!await rates.UpdateAsync(id, fields, stored[id].UpdatedAt, updatedBy, uow.Transaction, cancellationToken))
            {
                throw new ConcurrencyException();
            }
        }
        foreach (var fields in newRows)
        {
            await rates.InsertAsync(fields, updatedBy, uow.Transaction, cancellationToken);
        }

        var after = await ReadAsync(from, to, currencyCode, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return after;
    }

    /// <summary>
    /// BC-BR-412: the default rate of a document in the currency on the date. 1 for the base currency, else the latest rate on or
    /// before the date, or null when there is none and the user must enter one.
    /// </summary>
    public async Task<decimal?> DefaultRateAsync(string currencyCode, DateOnly date, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var code = currencyCode.Trim().ToUpperInvariant();
        var baseCurrency = (await localization.FindCompanyAsync(transaction, cancellationToken))?.BaseCurrencyCode;
        var stored = string.Equals(code, baseCurrency, StringComparison.Ordinal)
            ? null
            : await rates.FindLatestAsync(code, date, transaction, cancellationToken);
        return ExchangeRateRules.DefaultRate(code, baseCurrency, stored);
    }

    private async Task<IReadOnlyList<ExchangeRateDto>> ReadAsync(
        DateOnly? from, DateOnly? to, string? currencyCode, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var (start, end) = ExchangeRateRules.Period(from, to, clock.Today());
        var code = string.IsNullOrWhiteSpace(currencyCode) ? null : currencyCode.Trim().ToUpperInvariant();
        return (await rates.FindAsync(start, end, code, transaction, cancellationToken))
            .Select(r => new ExchangeRateDto(r.Id, r.RateDt, r.CurrencyCode, r.Rate, r.UpdatedAt))
            .ToList();
    }
}
