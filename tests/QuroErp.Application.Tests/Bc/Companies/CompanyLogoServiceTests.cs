using System.Data;
using FluentAssertions;
using QuroErp.Application.Bc.Companies;
using QuroErp.Application.Common;
using QuroErp.Application.Tests.Common.Auth;
using QuroErp.Domain.Bc.Companies;
using QuroErp.Domain.Common;

namespace QuroErp.Application.Tests.Bc.Companies;

public class CompanyLogoServiceTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01];

    private readonly FakeUnitOfWorkFactory _unitOfWork = new();
    private readonly FakeCompanyRepository _companies = new();

    private CompanyLogoService Service => new(_companies, _unitOfWork);

    [Fact, Trait("Id", "BC-007-T10")]
    public async Task UploadLogoAsync_Png_StoresDetectedTypeAndCommits()
    {
        _companies.HasCompany = true;

        await Service.UploadLogoAsync(Png, "kim", CancellationToken.None);

        _companies.Logo.Should().BeEquivalentTo(new CompanyLogoRow(Png, CompanyLogo.Png));
        _companies.UpdatedBy.Should().Be("kim");
        _unitOfWork.Commits.Should().Be(1);
    }

    [Fact, Trait("Id", "BC-007-T11")]
    public async Task UploadLogoAsync_NotAnImage_ThrowsBcE004BeforeOpeningTransaction()
    {
        _companies.HasCompany = true;

        var act = () => Service.UploadLogoAsync([1, 2, 3], "kim", CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(CompanyLogo.InvalidTypeCode);
        _companies.Logo.Should().BeNull();
        _unitOfWork.Commits.Should().Be(0);
    }

    [Fact, Trait("Id", "BC-007-T12")]
    public async Task UploadLogoAsync_NoCompany_ThrowsNotFoundBcE006()
    {
        var act = () => Service.UploadLogoAsync(Png, "kim", CancellationToken.None);

        (await act.Should().ThrowAsync<NotFoundException>()).Which.Code.Should().Be(CompanyLogoService.CompanyNotFoundCode);
        _unitOfWork.Commits.Should().Be(0);
    }

    [Fact, Trait("Id", "BC-007-T13")]
    public async Task GetLogoAsync_NoLogo_ThrowsNotFoundBcE007()
    {
        _companies.HasCompany = true;

        var act = () => Service.GetLogoAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<NotFoundException>()).Which.Code.Should().Be(CompanyLogoService.LogoNotFoundCode);
    }

    [Fact, Trait("Id", "BC-007-T14")]
    public async Task GetLogoAsync_WithLogo_ReturnsBytesAndType()
    {
        _companies.HasCompany = true;
        _companies.Logo = new CompanyLogoRow(Png, CompanyLogo.Png);

        var file = await Service.GetLogoAsync(CancellationToken.None);

        file.Content.Should().Equal(Png);
        file.ContentType.Should().Be(CompanyLogo.Png);
    }

    private sealed class FakeCompanyRepository : ICompanyRepository
    {
        public bool HasCompany { get; set; }

        public CompanyLogoRow? Logo { get; set; }

        public string? UpdatedBy { get; private set; }

        public Task<CompanyLogoRow?> FindLogoAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(HasCompany ? Logo : null);

        public Task<bool> UpdateLogoAsync(
            byte[] image, string contentType, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            if (HasCompany)
            {
                Logo = new CompanyLogoRow(image, contentType);
                UpdatedBy = updatedBy;
            }
            return Task.FromResult(HasCompany);
        }
    }
}
