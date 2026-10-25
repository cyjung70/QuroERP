using System.Data;
using Dapper;
using QuroErp.Application.Bc.Companies;

namespace QuroErp.Infrastructure.Bc.Companies;

internal sealed class CompanyRepository : ICompanyRepository
{
    // Single company (ADR-0025): the lowest id is the company
    private const string SelectLogo = """
        SELECT logo_image, logo_content_type
        FROM bc_company
        WHERE logo_image IS NOT NULL
          AND id = (SELECT min(id) FROM bc_company)
        """;

    private const string UpdateLogo = """
        UPDATE bc_company
        SET logo_image = @Image, logo_content_type = @ContentType, updated_at = now(), updated_by = @UpdatedBy
        WHERE id = (SELECT min(id) FROM bc_company)
        """;

    public Task<CompanyLogoRow?> FindLogoAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.QuerySingleOrDefaultAsync<CompanyLogoRow>(
            new CommandDefinition(SelectLogo, transaction: transaction, cancellationToken: cancellationToken));

    public async Task<bool> UpdateLogoAsync(
        byte[] image, string contentType, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var affected = await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            UpdateLogo, new { Image = image, ContentType = contentType, UpdatedBy = updatedBy }, transaction, cancellationToken: cancellationToken));
        return affected > 0;
    }
}
