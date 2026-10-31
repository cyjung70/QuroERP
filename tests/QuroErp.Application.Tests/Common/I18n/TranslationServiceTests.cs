using System.Data;
using FluentAssertions;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Common.I18n;
using QuroErp.Application.Tests.Bc.Localization;

namespace QuroErp.Application.Tests.Common.I18n;

public class TranslationServiceTests
{
    private readonly FakeTranslationRepository _repository = new(
    [
        new(1, "ko", "등급"), new(1, "en", "Grade"),
        new(2, "en", "Department"),
        new(3, "ko", null),
        new(4, "ko", "창고"), new(4, "sw", "Ghala"),
    ]);

    private readonly FakeLocalizationRepository _localization = new();

    private TranslationService Service => new(_repository, _localization);

    [Fact, Trait("Id", "CORE-004-T06")]
    public async Task GetNamesAsync_En_ReturnsEnOrFallbackAndSkipsNoName()
    {
        var names = await Service.GetNamesAsync(
            TranslationTarget.CodeMajor, [1, 2, 3, 5], "en", null!, CancellationToken.None);

        names.Should().BeEquivalentTo(new Dictionary<long, string> { [1] = "Grade", [2] = "Department" });
        _repository.LastTarget.Should().Be(TranslationTarget.CodeMajor);
    }

    [Fact, Trait("Id", "CORE-004-T07")]
    public async Task GetNamesAsync_Ko_FallsBackToEnWhenKoMissing()
    {
        var names = await Service.GetNamesAsync(
            TranslationTarget.CodeMajor, [1, 2], "ko", null!, CancellationToken.None);

        names.Should().BeEquivalentTo(new Dictionary<long, string> { [1] = "등급", [2] = "Department" });
    }

    [Fact, Trait("Id", "CORE-004-T08")]
    public async Task GetNamesAsync_NoIds_DoesNotQuery()
    {
        var names = await Service.GetNamesAsync(
            TranslationTarget.ItemName, [], "ko", null!, CancellationToken.None);

        names.Should().BeEmpty();
        _repository.LastTarget.Should().BeNull();
    }

    [Fact, Trait("Id", "BC-UT-402")]
    public async Task GetNamesAsync_RequestedMissing_UsesCompanyDefaultLanguageBeforeEn()
    {
        _localization.Company = new CompanyLocalizationRow("KR", "KRW", "ko", "DOCUMENT", "Asia/Seoul");

        var names = await Service.GetNamesAsync(
            TranslationTarget.CodeMajor, [1, 2, 4], "sw", null!, CancellationToken.None);

        names.Should().BeEquivalentTo(new Dictionary<long, string> { [1] = "등급", [2] = "Department", [4] = "Ghala" });
    }

    private sealed class FakeTranslationRepository(IReadOnlyList<TranslationRow> rows) : ITranslationRepository
    {
        public TranslationTarget? LastTarget { get; private set; }

        public Task<IReadOnlyList<TranslationRow>> FindAsync(
            TranslationTarget target, IReadOnlyCollection<long> ownerIds, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            LastTarget = target;
            return Task.FromResult<IReadOnlyList<TranslationRow>>(rows.Where(r => ownerIds.Contains(r.OwnerId)).ToList());
        }
    }
}
