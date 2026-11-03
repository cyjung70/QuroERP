using System.Data;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Codes;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Bc.Tax;
using QuroErp.Domain.Bc.Tax;
using QuroErp.Domain.Common;

namespace QuroErp.Application.Bc.Tax;

/// <summary>
/// US tax areas (BC-UC-029, BC-BR-511, BC-BR-512): list, find, add, change and delete a header with its rate lines in one transaction.
/// The rate on a document date is read by <see cref="ITaxRateProvider"/> (BC-BR-513, BC-BR-514).
/// </summary>
public sealed class TaxAreaService(ITaxAreaRepository areas, ICodeValidator codes, IBusinessClock clock, IUnitOfWorkFactory unitOfWork)
{
    /// <summary>The tax area does not exist. Arg: id.</summary>
    public const string NotFoundCode = "Common-E-006";

    /// <summary>The state is an active Minor code of this Major.</summary>
    public const string StateMajor = "STATE";

    public async Task<IReadOnlyList<TaxAreaSummaryDto>> SearchAsync(TaxAreaSearchQuery query, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var rows = await areas.SearchAsync(
            Clean(query.Keyword), Clean(query.StateCode)?.ToUpperInvariant(), clock.Today(), TaxAreaRoutes.MaxListSize, uow.Transaction,
            cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return rows.Select(r => new TaxAreaSummaryDto(r.Id, r.TaxAreaCode, r.TaxAreaName, r.StateCode, r.EffectiveFrom, r.Rate)).ToList();
    }

    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task<TaxAreaDto> GetAsync(long id, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var dto = await ReadAsync(id, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return dto;
    }

    /// <exception cref="DomainException">422 BC-E-511, BC-E-512, or Common-E-002 for a state that is not an active STATE code.</exception>
    public async Task<TaxAreaDto> CreateAsync(SaveTaxAreaRequest request, string user, CancellationToken cancellationToken)
    {
        var fields = await PrepareAsync(request, cancellationToken);
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        await areas.LockAsync(uow.Transaction, cancellationToken);
        TaxAreaRules.EnsureUnique(fields.TaxAreaCode, await areas.FindIdByCodeAsync(fields.TaxAreaCode, uow.Transaction, cancellationToken) is not null);
        var id = await areas.InsertAsync(fields, user, uow.Transaction, cancellationToken);
        await areas.SaveRatesAsync(id, fields, user, uow.Transaction, cancellationToken);
        var dto = await ReadAsync(id, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return dto;
    }

    /// <summary>Saves the header; the sent lines replace the stored ones.</summary>
    /// <exception cref="DomainException">The checks of <see cref="CreateAsync"/>.</exception>
    /// <exception cref="ConcurrencyException">409 Common-E-004 when updatedAt is missing or differs from the stored row.</exception>
    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task<TaxAreaDto> UpdateAsync(long id, SaveTaxAreaRequest request, string user, CancellationToken cancellationToken)
    {
        var fields = await PrepareAsync(request, cancellationToken);
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        await areas.LockAsync(uow.Transaction, cancellationToken);
        var row = await LockAsync(id, request.UpdatedAt ?? default, uow.Transaction, cancellationToken);
        TaxAreaRules.EnsureUnique(
            fields.TaxAreaCode, await areas.FindIdByCodeAsync(fields.TaxAreaCode, uow.Transaction, cancellationToken) is { } other && other != id);
        if (!await areas.UpdateAsync(id, fields, row.UpdatedAt, user, uow.Transaction, cancellationToken))
        {
            throw new ConcurrencyException();
        }
        await areas.SaveRatesAsync(id, fields, user, uow.Transaction, cancellationToken);
        var dto = await ReadAsync(id, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return dto;
    }

    /// <summary>Deletes the area and its lines.</summary>
    /// <exception cref="ConflictException">409 BC-E-511 when a tax code uses the area.</exception>
    /// <exception cref="ConcurrencyException">409 Common-E-004 when updatedAt differs from the stored row.</exception>
    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task DeleteAsync(long id, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        await areas.LockAsync(uow.Transaction, cancellationToken);
        var row = await LockAsync(id, updatedAt, uow.Transaction, cancellationToken);
        if (await areas.IsUsedByTaxCodesAsync(id, uow.Transaction, cancellationToken))
        {
            throw new ConflictException(TaxAreaRules.InvalidCode, row.TaxAreaCode);
        }
        if (!await areas.DeleteAsync(id, updatedAt, uow.Transaction, cancellationToken))
        {
            throw new ConcurrencyException();
        }
        await uow.CommitAsync(cancellationToken);
    }

    // The Domain checks the fields; the state check reads codes in its own unit of work, before the save transaction
    private async Task<TaxAreaFields> PrepareAsync(SaveTaxAreaRequest request, CancellationToken cancellationToken)
    {
        var fields = TaxAreaRules.Normalize(
            request.TaxAreaCode, request.TaxAreaName, request.StateCode,
            (request.Rates ?? []).Select(r => new TaxAreaRateInput(r.EffectiveFrom, r.Rate)));
        await codes.EnsureValidAsync(StateMajor, fields.StateCode, cancellationToken);
        return fields;
    }

    private async Task<TaxAreaRecord> LockAsync(long id, DateTimeOffset updatedAt, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var row = await areas.FindAsync(id, forUpdate: true, transaction, cancellationToken) ?? throw new NotFoundException(NotFoundCode, id);
        return row.UpdatedAt == updatedAt ? row : throw new ConcurrencyException();
    }

    private async Task<TaxAreaDto> ReadAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var row = await areas.FindAsync(id, forUpdate: false, transaction, cancellationToken) ?? throw new NotFoundException(NotFoundCode, id);
        var rates = await areas.FindRatesAsync(id, transaction, cancellationToken);
        return new TaxAreaDto(
            row.Id, row.TaxAreaCode, row.TaxAreaName, row.StateCode, [.. rates.Select(r => new TaxAreaRateDto(r.EffectiveFrom, r.Rate))],
            row.UpdatedAt);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
