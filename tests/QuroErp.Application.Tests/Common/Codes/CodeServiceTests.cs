using System.Data;
using FluentAssertions;
using QuroErp.Application.Common;
using QuroErp.Application.Tests.Bc.Localization;
using QuroErp.Application.Common.Codes;
using QuroErp.Application.Common.I18n;
using QuroErp.Application.Tests.Common.Auth;
using QuroErp.Contracts.Common.Codes;
using QuroErp.Domain.Common;

namespace QuroErp.Application.Tests.Common.Codes;

public class CodeServiceTests
{
    private readonly FakeUnitOfWorkFactory _unitOfWork = new();

    private readonly FakeCodeRepository _codes = new(new Dictionary<string, List<CodeMinorRow>>
    {
        ["VALID_FLAG"] = [new(1, "Y", 10), new(2, "N", 20), new(3, "S", 30)],
    });

    private readonly FakeTranslationRepository _translations = new(
    [
        new(1, "ko", "사용"), new(1, "en", "Used"),
        new(2, "ko", "사용 안 함"), new(2, "en", "Not Used"),
    ]);

    private CodeService Service => new(_codes, new TranslationService(_translations, new FakeLocalizationRepository()), _unitOfWork);

    private CodeValidator Validator => new(_codes, _unitOfWork);

    [Fact, Trait("Id", "CORE-005-T01")]
    public async Task EnsureValidAsync_ActiveCode_Passes()
    {
        var act = () => Validator.EnsureValidAsync("VALID_FLAG", "Y", CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Theory, Trait("Id", "CORE-005-T02")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task EnsureValidAsync_NullOrBlank_PassesWithoutQuery(string? value)
    {
        await Validator.EnsureValidAsync("VALID_FLAG", value, CancellationToken.None);

        _codes.Queries.Should().Be(0);
    }

    [Theory, Trait("Id", "CORE-005-T03")]
    [InlineData("VALID_FLAG", "X")]
    [InlineData("NO_SUCH_MAJOR", "Y")]
    public async Task EnsureValidAsync_UnknownCode_ThrowsCommonE002WithArgs(string majorCode, string value)
    {
        var act = () => Validator.EnsureValidAsync(majorCode, value, CancellationToken.None);

        var e = (await act.Should().ThrowAsync<DomainException>()).Which;
        e.Code.Should().Be(CodeMessages.InvalidCode);
        e.Args.Should().Equal(majorCode, value);
    }

    [Fact, Trait("Id", "CORE-005-T04")]
    public async Task GetCodesAsync_En_ReturnsSortedTranslatedNamesAndFallsBackToMinorCode()
    {
        var codes = await Service.GetCodesAsync("VALID_FLAG", "en", CancellationToken.None);

        codes.Should().Equal(new CodeItemDto("Y", "Used"), new CodeItemDto("N", "Not Used"), new CodeItemDto("S", "S"));
    }

    [Fact, Trait("Id", "CORE-005-T05")]
    public async Task GetCodesAsync_Ko_ReturnsKoNames()
    {
        var codes = await Service.GetCodesAsync("VALID_FLAG", "ko", CancellationToken.None);

        codes.Select(c => c.Name).Should().Equal("사용", "사용 안 함", "S");
    }

    [Fact, Trait("Id", "CORE-005-T06")]
    public async Task GetCodesAsync_UnknownMajor_ThrowsNotFoundCommonE003()
    {
        var act = () => Service.GetCodesAsync("NO_SUCH_MAJOR", "ko", CancellationToken.None);

        var e = (await act.Should().ThrowAsync<NotFoundException>()).Which;
        e.Code.Should().Be(CodeMessages.UnknownMajor);
        e.Args.Should().Equal("NO_SUCH_MAJOR");
    }

    /// <summary>Holds active codes only, the same as what the real repository returns.</summary>
    private sealed class FakeCodeRepository(Dictionary<string, List<CodeMinorRow>> majors) : ICodeRepository
    {
        public int Queries { get; private set; }

        public Task<IReadOnlyList<CodeMinorRow>?> FindActiveMinorsAsync(
            string majorCode, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Queries++;
            return Task.FromResult<IReadOnlyList<CodeMinorRow>?>(majors.GetValueOrDefault(majorCode));
        }

        public Task<bool> ExistsActiveAsync(string majorCode, string minorCode, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Queries++;
            return Task.FromResult(majors.TryGetValue(majorCode, out var minors) && minors.Any(m => m.MinorCode == minorCode));
        }
    }

    private sealed class FakeTranslationRepository(IReadOnlyList<TranslationRow> rows) : ITranslationRepository
    {
        public Task<IReadOnlyList<TranslationRow>> FindAsync(
            TranslationTarget target, IReadOnlyCollection<long> ownerIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TranslationRow>>(rows.Where(r => ownerIds.Contains(r.OwnerId)).ToList());
    }
}
