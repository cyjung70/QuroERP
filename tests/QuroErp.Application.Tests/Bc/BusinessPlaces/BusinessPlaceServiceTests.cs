using System.Data;
using FluentAssertions;
using QuroErp.Application.Bc.BusinessPlaces;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Common;
using QuroErp.Application.Tests.Bc.Localization;
using QuroErp.Application.Tests.Common.Auth;
using QuroErp.Contracts.Bc.BusinessPlaces;
using QuroErp.Domain.Bc.BusinessPlaces;
using QuroErp.Domain.Common;

namespace QuroErp.Application.Tests.Bc.BusinessPlaces;

/// <summary>Business places on the company screen (BC-BR-319, BC-BR-320) with fake storage.</summary>
public class BusinessPlaceServiceTests
{
    private static readonly DateTimeOffset Read = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken None = CancellationToken.None;

    private static readonly UpdateBusinessPlaceRequest Request = new(
        "Quro", "220-81-62517", null, "Kim", "Service", "Software", null, null, null, null, null, null, true, Read);

    private readonly FakePlaces _places = new();
    private readonly FakeLocalizationRepository _localization = new() { Company = new("KR", "KRW", "ko", "DOCUMENT", "Asia/Seoul") };
    private readonly FakeUnitOfWorkFactory _uow = new();
    private readonly BusinessPlaceService _service;

    public BusinessPlaceServiceTests() => _service = new BusinessPlaceService(_places, _localization, _uow);

    [Fact, Trait("Id", "BC-UT-320")]
    public async Task Update_SavesNormalizedValues_AndCommits()
    {
        var saved = await _service.UpdateAsync(1, Request with { BusinessPlaceName = " Quro Seoul " }, "kim", None);

        (saved.BusinessPlaceName, saved.BusinessRegNo, saved.UpdatedBy).Should().Be(("Quro Seoul", "2208162517", "kim"));
        _uow.Commits.Should().Be(1);
    }

    [Fact, Trait("Id", "BC-UT-320"), Trait("Rule", "BC-BR-320")]
    public async Task Update_KrCompanyWithoutARegistrationNumber_IsBcE320()
    {
        var act = () => _service.UpdateAsync(1, Request with { BusinessRegNo = null }, "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(BusinessPlaceRules.InvalidFieldCode);
        _places.Writes.Should().Be(0);
    }

    [Fact, Trait("Id", "BC-UT-320"), Trait("Rule", "BC-BR-320")]
    public async Task Update_UsCompanyWithoutARegistrationNumber_IsAllowed()
    {
        _localization.Company = new("US", "USD", "en", "DOCUMENT", "America/New_York");

        var saved = await _service.UpdateAsync(1, Request with { BusinessRegNo = null }, "kim", None);

        saved.BusinessRegNo.Should().BeNull();
    }

    [Fact, Trait("Id", "BC-UT-320")]
    public async Task Update_WithAStaleUpdatedAt_Is409()
    {
        var act = () => _service.UpdateAsync(1, Request with { UpdatedAt = Read.AddDays(-1) }, "kim", None);

        await act.Should().ThrowAsync<ConcurrencyException>();
        _places.Writes.Should().Be(0);
    }

    [Fact, Trait("Id", "BC-UT-320")]
    public async Task Update_AnUnknownPlace_IsNotFound()
    {
        var act = () => _service.UpdateAsync(9, Request, "kim", None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private sealed class FakePlaces : IBusinessPlaceRepository
    {
        public BusinessPlaceDto Place { get; private set; } = new(
            1, "MAIN", "Quro", true, null, null, null, null, null, null, null, null, null, null, null, true, Read, "system", Read, "system");

        public int Writes { get; private set; }

        public Task<IReadOnlyList<BusinessPlaceDto>> ListAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BusinessPlaceDto>>([Place]);

        public Task<BusinessPlaceDto?> FindAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(id == Place.Id ? Place : null);

        public Task<bool> UpdateAsync(
            long id, BusinessPlaceFields fields, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction,
            CancellationToken cancellationToken)
        {
            Writes++;
            Place = Place with
            {
                BusinessPlaceName = fields.BusinessPlaceName!,
                BusinessRegNo = fields.BusinessRegNo,
                UpdatedAt = Place.UpdatedAt.AddMinutes(1),
                UpdatedBy = updatedBy,
            };
            return Task.FromResult(true);
        }

        public Task<long?> FindDefaultIdAsync(IDbTransaction transaction, CancellationToken cancellationToken) => Task.FromResult<long?>(Place.Id);
    }
}
