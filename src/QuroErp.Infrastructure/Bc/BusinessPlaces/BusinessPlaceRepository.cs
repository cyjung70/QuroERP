using System.Data;
using Dapper;
using QuroErp.Application.Bc.BusinessPlaces;
using QuroErp.Contracts.Bc.BusinessPlaces;
using QuroErp.Domain.Bc.BusinessPlaces;

namespace QuroErp.Infrastructure.Bc.BusinessPlaces;

internal sealed class BusinessPlaceRepository : IBusinessPlaceRepository
{
    private const string Columns = """
        SELECT id, business_place_code, business_place_name, is_default, business_reg_no, sub_business_no, ceo_name, business_type,
               business_item, addr1, addr2, city, state, zip_code, tel_no, use_yn, created_at, created_by, updated_at, updated_by
        FROM bc_business_place
        """;

    private const string SelectAll = Columns + " ORDER BY is_default DESC, business_place_code";

    private const string SelectById = Columns + " WHERE id = @Id";

    private const string LockById = SelectById + " FOR UPDATE";

    private const string SelectDefaultId = "SELECT id FROM bc_business_place WHERE is_default";

    private const string Update = """
        UPDATE bc_business_place
        SET business_place_name = @BusinessPlaceName, business_reg_no = @BusinessRegNo, sub_business_no = @SubBusinessNo,
            ceo_name = @CeoName, business_type = @BusinessType, business_item = @BusinessItem, addr1 = @Addr1, addr2 = @Addr2,
            city = @City, state = @State, zip_code = @ZipCode, tel_no = @TelNo, use_yn = @UseYn, updated_at = now(), updated_by = @UpdatedBy
        WHERE id = @Id AND updated_at = @ExpectedUpdatedAt
        """;

    public async Task<IReadOnlyList<BusinessPlaceDto>> ListAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<Row>(
            new CommandDefinition(SelectAll, transaction: transaction, cancellationToken: cancellationToken)))
        .Select(r => r.ToDto()).ToList();

    public async Task<BusinessPlaceDto?> FindAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var row = await transaction.Connection!.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            forUpdate ? LockById : SelectById, new { Id = id }, transaction, cancellationToken: cancellationToken));
        return row?.ToDto();
    }

    public async Task<bool> UpdateAsync(
        long id, BusinessPlaceFields fields, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction,
        CancellationToken cancellationToken) =>
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Update,
            new
            {
                Id = id,
                fields.BusinessPlaceName,
                fields.BusinessRegNo,
                fields.SubBusinessNo,
                fields.CeoName,
                fields.BusinessType,
                fields.BusinessItem,
                fields.Addr1,
                fields.Addr2,
                fields.City,
                fields.State,
                fields.ZipCode,
                fields.TelNo,
                fields.UseYn,
                UpdatedBy = updatedBy,
                ExpectedUpdatedAt = expectedUpdatedAt.UtcDateTime,
            },
            transaction,
            cancellationToken: cancellationToken)) == 1;

    public Task<long?> FindDefaultIdAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<long?>(
            new CommandDefinition(SelectDefaultId, transaction: transaction, cancellationToken: cancellationToken));

    // Npgsql reads timestamptz as a UTC DateTime; Dapper does not convert it to DateTimeOffset for constructor binding
    private sealed record Row(
        long Id, string BusinessPlaceCode, string BusinessPlaceName, bool IsDefault, string? BusinessRegNo, string? SubBusinessNo,
        string? CeoName, string? BusinessType, string? BusinessItem, string? Addr1, string? Addr2, string? City, string? State,
        string? ZipCode, string? TelNo, bool UseYn, DateTime CreatedAt, string CreatedBy, DateTime UpdatedAt, string UpdatedBy)
    {
        public BusinessPlaceDto ToDto() => new(
            Id, BusinessPlaceCode, BusinessPlaceName, IsDefault, BusinessRegNo, SubBusinessNo, CeoName, BusinessType, BusinessItem, Addr1,
            Addr2, City, State, ZipCode, TelNo, UseYn, Utc(CreatedAt), CreatedBy, Utc(UpdatedAt), UpdatedBy);

        private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}
