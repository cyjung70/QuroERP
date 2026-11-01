using System.Data;
using FluentAssertions;
using QuroErp.Application.Bc.Tax;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Codes;
using QuroErp.Application.Tests.Common;
using QuroErp.Application.Tests.Common.Auth;
using QuroErp.Contracts.Bc.Tax;
using QuroErp.Domain.Bc.Tax;
using QuroErp.Domain.Common;

namespace QuroErp.Application.Tests.Bc.Tax;

/// <summary>US tax areas (BC-UC-029, BC-BR-511, BC-BR-512) with fake storage.</summary>
public class TaxAreaServiceTests
{
    private static readonly DateTimeOffset Read = new(2026, 10, 30, 0, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly DateOnly Jan1 = new(2026, 1, 1);

    private readonly FakeTaxAreaRepository _areas = new();
    private readonly FakeUnitOfWorkFactory _uow = new();
    private readonly FakeBusinessClock _clock = new() { TodayValue = new(2026, 10, 31) };
    private readonly TaxAreaService _service;

    public TaxAreaServiceTests()
    {
        _service = new TaxAreaService(_areas, new FakeCodeValidator(), _clock, _uow);
        _areas.Rows[10] = new(10, "NYC", "New York City", "NY", Read);
        _areas.Rows[11] = new(11, "LA", "Los Angeles", "CA", Read);
        _areas.UsedByTaxCodes.Add(11);
    }

    private static SaveTaxAreaRequest Request(string code, string state = "NY", DateTimeOffset? updatedAt = null) =>
        new(code, "Area", state, [new SaveTaxAreaRateRow(Jan1, 8.875m)], updatedAt);

    [Fact, Trait("Id", "BC-UC-029")]
    public async Task Search_PassesTheCleanConditions_AndTodayInTheCompanyTimeZone()
    {
        await _service.SearchAsync(new TaxAreaSearchQuery("  york ", " ny "), None);
        await _service.SearchAsync(new TaxAreaSearchQuery(" ", null), None);

        _areas.Searches.Should().Equal(("york", "NY", _clock.TodayValue, TaxAreaRoutes.MaxListSize), (null, null, _clock.TodayValue, TaxAreaRoutes.MaxListSize));
    }

    [Fact, Trait("Id", "BC-UC-029"), Trait("Rule", "BC-BR-512")]
    public async Task Create_SavesTheHeaderAndLines_ThenCommitsOnce()
    {
        var area = await _service.CreateAsync(Request(" sf ", "ca"), "kim", None);

        area.Should().BeEquivalentTo(new { TaxAreaCode = "SF", StateCode = "CA" });
        area.Rates.Should().Equal(new TaxAreaRateDto(Jan1, 8.875m));
        _areas.Locks.Should().Be(1);
        _uow.Commits.Should().Be(1);
    }

    [Theory, Trait("Id", "BC-UT-511"), Trait("Rule", "BC-BR-511")]
    [InlineData("nyc", "NY", "BC-E-511")]
    [InlineData("SF", "ZZ", "Common-E-002")]
    public async Task Create_ATakenCodeOrAnUnknownState_SavesNothing(string code, string state, string error)
    {
        var act = () => _service.CreateAsync(Request(code, state), "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(error);
        _areas.Rows.Should().HaveCount(2);
        _uow.Commits.Should().Be(0);
    }

    [Fact, Trait("Id", "BC-UC-029")]
    public async Task Update_KeepingItsOwnCode_SavesAndReplacesTheLines()
    {
        _areas.Rates[10] = [new(new DateOnly(2025, 1, 1), 8m)];

        var area = await _service.UpdateAsync(10, Request("NYC", updatedAt: Read), "lee", None);

        area.Rates.Should().Equal(new TaxAreaRateDto(Jan1, 8.875m));
        _uow.Commits.Should().Be(1);
    }

    [Fact, Trait("Id", "BC-UT-511"), Trait("Rule", "BC-BR-511")]
    public async Task Update_ToTheCodeOfAnotherArea_ThrowsBcE511()
    {
        var act = () => _service.UpdateAsync(10, Request("la", updatedAt: Read), "lee", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(TaxAreaRules.InvalidCode);
        _uow.Commits.Should().Be(0);
    }

    [Fact, Trait("Id", "BC-UC-029")]
    public async Task Update_WithoutOrWithAStaleUpdatedAt_ThrowsConcurrency_AndAnUnknownIdNotFound()
    {
        await FluentActions.Invoking(() => _service.UpdateAsync(10, Request("NYC"), "lee", None)).Should().ThrowAsync<ConcurrencyException>();
        await FluentActions.Invoking(() => _service.UpdateAsync(10, Request("NYC", updatedAt: Read.AddSeconds(-1)), "lee", None))
            .Should().ThrowAsync<ConcurrencyException>();
        (await FluentActions.Invoking(() => _service.UpdateAsync(99, Request("NYC", updatedAt: Read), "lee", None))
            .Should().ThrowAsync<NotFoundException>()).Which.Code.Should().Be(TaxAreaService.NotFoundCode);
    }

    [Fact, Trait("Id", "BC-UC-029")]
    public async Task Delete_AnAreaTaxCodesUse_Throws409BcE511_AndAnUnusedOneIsDeleted()
    {
        var used = () => _service.DeleteAsync(11, Read, None);

        var conflict = (await used.Should().ThrowAsync<ConflictException>()).Which;
        conflict.Code.Should().Be(TaxAreaRules.InvalidCode);
        conflict.Args.Should().Equal("LA");
        await _service.DeleteAsync(10, Read, None);
        _areas.Rows.Keys.Should().Equal(11);
        _uow.Commits.Should().Be(1);
    }

    private sealed class FakeCodeValidator : ICodeValidator
    {
        public Task EnsureValidAsync(string majorCode, string? value, CancellationToken cancellationToken) =>
            majorCode == TaxAreaService.StateMajor && value is not ("NY" or "CA")
                ? throw new DomainException("Common-E-002", majorCode, value ?? "")
                : Task.CompletedTask;
    }

    private sealed class FakeTaxAreaRepository : ITaxAreaRepository
    {
        public Dictionary<long, TaxAreaRecord> Rows { get; } = [];

        public Dictionary<long, IReadOnlyList<TaxAreaRate>> Rates { get; } = [];

        public HashSet<long> UsedByTaxCodes { get; } = [];

        public List<(string? Keyword, string? StateCode, DateOnly Today, int Limit)> Searches { get; } = [];

        public int Locks { get; private set; }

        public Task LockAsync(IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Locks++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TaxAreaSummaryRecord>> SearchAsync(
            string? keyword, string? stateCode, DateOnly today, int limit, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Searches.Add((keyword, stateCode, today, limit));
            return Task.FromResult<IReadOnlyList<TaxAreaSummaryRecord>>([]);
        }

        public Task<TaxAreaRecord?> FindAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(Rows.GetValueOrDefault(id));

        public Task<IReadOnlyList<TaxAreaRate>> FindRatesAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(Rates.GetValueOrDefault(id) ?? []);

        public Task<long?> FindIdByCodeAsync(string taxAreaCode, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(Rows.Values.Where(r => r.TaxAreaCode == taxAreaCode).Select(r => (long?)r.Id).FirstOrDefault());

        public Task<bool> IsUsedByTaxCodesAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(UsedByTaxCodes.Contains(id));

        public Task<long> InsertAsync(TaxAreaFields fields, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            var id = Rows.Keys.Max() + 1;
            Rows[id] = new(id, fields.TaxAreaCode, fields.TaxAreaName, fields.StateCode, Read);
            return Task.FromResult(id);
        }

        public Task<bool> UpdateAsync(
            long id, TaxAreaFields fields, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction,
            CancellationToken cancellationToken)
        {
            Rows[id] = new(id, fields.TaxAreaCode, fields.TaxAreaName, fields.StateCode, expectedUpdatedAt.AddSeconds(1));
            return Task.FromResult(true);
        }

        public Task SaveRatesAsync(long id, TaxAreaFields fields, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Rates[id] = fields.Rates;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(long id, DateTimeOffset expectedUpdatedAt, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(Rows.Remove(id));
    }
}
