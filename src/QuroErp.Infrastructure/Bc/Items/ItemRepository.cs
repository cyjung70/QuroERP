using System.Data;
using Dapper;
using QuroErp.Application.Bc.Items;
using QuroErp.Domain.Bc.Items;

namespace QuroErp.Infrastructure.Bc.Items;

internal sealed class ItemRepository : IItemRepository
{
    private const string Columns = """
        id, item_cd, item_name, item_name2, item_desc, type AS item_type, unit, class AS item_class, valid_flag, usd_prc, so_usd_prc,
        upc_code, location, reorder_point, memo1, memo2, memo3, memo4, memo5, item_grade, taxable, vat_kind, plating, item_group_cd,
        mvg_cost, onhand_qty, stk_val, created_at, created_by, updated_at, updated_by
        """;

    private const string SelectById = "SELECT " + Columns + " FROM bc_item WHERE id = @Id";

    private const string SelectByIdForUpdate = SelectById + " FOR UPDATE";

    private const string SelectCodeExists = "SELECT EXISTS (SELECT 1 FROM bc_item WHERE item_cd = @ItemCd)";

    // Item class, item code order as in the list (class NULLS FIRST): stored text is never blank, so '' sorts as null
    private const string FromAfterCurrent = """
         FROM bc_item, (SELECT coalesce(class, '') AS cur_class, item_cd AS cur_cd FROM bc_item WHERE id = @Id) cur
        """;

    private const string SelectNext = "SELECT " + Columns + FromAfterCurrent + """

        WHERE (coalesce(class, ''), item_cd) > (cur.cur_class, cur.cur_cd)
        ORDER BY coalesce(class, ''), item_cd
        LIMIT 1
        """;

    private const string SelectPrevious = "SELECT " + Columns + FromAfterCurrent + """

        WHERE (coalesce(class, ''), item_cd) < (cur.cur_class, cur.cur_cd)
        ORDER BY coalesce(class, '') DESC, item_cd DESC
        LIMIT 1
        """;

    private const string SearchCondition = """
        WHERE (@ItemCdPattern IS NULL OR item_cd ILIKE @ItemCdPattern ESCAPE '\')
          AND (@NamePattern IS NULL OR item_name ILIKE @NamePattern ESCAPE '\' OR item_name2 ILIKE @NamePattern ESCAPE '\')
          AND (@ItemClass IS NULL OR class = @ItemClass)
          AND (@ValidFlag IS NULL OR valid_flag = @ValidFlag)
        """;

    private const string Search = "SELECT " + Columns + " FROM bc_item " + SearchCondition + """

        ORDER BY class NULLS FIRST, item_cd
        LIMIT @Limit OFFSET @Offset
        """;

    private const string Count = "SELECT count(*) FROM bc_item " + SearchCondition;

    private const string Insert = """
        INSERT INTO bc_item (item_cd, item_name, item_name2, item_desc, type, unit, class, valid_flag, usd_prc, so_usd_prc, upc_code,
                             location, reorder_point, memo1, memo2, memo3, memo4, memo5, item_grade, taxable, vat_kind, plating,
                             item_group_cd, mvg_cost, onhand_qty, stk_val, created_by, updated_by)
        VALUES (@ItemCd, @ItemName, @ItemName2, @ItemDesc, @ItemType, @Unit, @ItemClass, @ValidFlag, @UsdPrc, @SoUsdPrc, @UpcCode,
                @Location, @ReorderPoint, @Memo1, @Memo2, @Memo3, @Memo4, @Memo5, @ItemGrade, @Taxable, @VatKind, @Plating,
                @ItemGroupCd, @MvgCost, @OnhandQty, @StkVal, @UserId, @UserId)
        RETURNING id
        """;

    // Moving average cost, stock quantity and value are left alone (MM-BR-804, MM-BR-805)
    private const string Update = """
        UPDATE bc_item
        SET item_name = @ItemName, item_name2 = @ItemName2, item_desc = @ItemDesc, type = @ItemType, unit = @Unit, class = @ItemClass,
            valid_flag = @ValidFlag, usd_prc = @UsdPrc, so_usd_prc = @SoUsdPrc, upc_code = @UpcCode, location = @Location,
            reorder_point = @ReorderPoint, memo1 = @Memo1, memo2 = @Memo2, memo3 = @Memo3, memo4 = @Memo4, memo5 = @Memo5,
            item_grade = @ItemGrade, taxable = @Taxable, vat_kind = @VatKind, item_group_cd = @ItemGroupCd,
            updated_at = now(), updated_by = @UserId
        WHERE id = @Id AND updated_at = @ExpectedUpdatedAt
        """;

    private const string Delete = "DELETE FROM bc_item WHERE id = @Id AND updated_at = @ExpectedUpdatedAt";

    // MM-BR-806: stock in any warehouse, stock movements, set parent or component
    private const string SelectTypeLocked = """
        SELECT EXISTS (SELECT 1 FROM mm_onhand WHERE item_id = @Id AND onhand_qty <> 0)
            OR EXISTS (SELECT 1 FROM mm_goods_movement WHERE item_id = @Id)
            OR EXISTS (SELECT 1 FROM sd_bom WHERE parent_item_id = @Id OR child_item_id = @Id)
        """;

    // MM-BR-807: every table that refers to the item, except its translations (deleted with it)
    private const string SelectInUse = """
        SELECT EXISTS (SELECT 1 FROM bc_item WHERE id = @Id AND alternate_item_group_id IS NOT NULL)
            OR EXISTS (SELECT 1 FROM bc_sales_price WHERE item_id = @Id)
            OR EXISTS (SELECT 1 FROM bc_purchase_price WHERE item_id = @Id)
            OR EXISTS (SELECT 1 FROM bc_cart_item WHERE item_id = @Id)
            OR EXISTS (SELECT 1 FROM mm_purchase_order_line WHERE item_id = @Id)
            OR EXISTS (SELECT 1 FROM mm_goods_receipt_line WHERE item_id = @Id)
            OR EXISTS (SELECT 1 FROM mm_goods_issue_line WHERE item_id = @Id)
            OR EXISTS (SELECT 1 FROM mm_goods_return_line WHERE item_id = @Id)
            OR EXISTS (SELECT 1 FROM mm_unplanned_receipt_line WHERE item_id = @Id)
            OR EXISTS (SELECT 1 FROM mm_stock_count_line WHERE item_id = @Id)
            OR EXISTS (SELECT 1 FROM mm_stock_transfer_line WHERE item_id = @Id)
            OR EXISTS (SELECT 1 FROM mm_purchase_simulation_line WHERE item_id = @Id)
            OR EXISTS (SELECT 1 FROM mm_goods_movement WHERE item_id = @Id)
            OR EXISTS (SELECT 1 FROM mm_monthly_inventory WHERE item_id = @Id)
            OR EXISTS (SELECT 1 FROM mm_onhand WHERE item_id = @Id)
            OR EXISTS (SELECT 1 FROM sd_sales_order_line WHERE item_id = @Id)
            OR EXISTS (SELECT 1 FROM sd_shipment_line WHERE item_id = @Id)
            OR EXISTS (SELECT 1 FROM sd_sales_invoice_line WHERE item_id = @Id)
            OR EXISTS (SELECT 1 FROM sd_bom WHERE parent_item_id = @Id OR child_item_id = @Id)
            OR EXISTS (SELECT 1 FROM sd_mrp_part WHERE item_id = @Id)
            OR EXISTS (SELECT 1 FROM sd_mrp_product WHERE item_id = @Id)
            OR EXISTS (SELECT 1 FROM sd_production_result_line WHERE item_id = @Id)
        """;

    // MM-BR-820, SD-BR-607: own image first, then the item group's plating R or empty, then by item code; legacy Clear left empty images
    private const string SelectImage = """
        SELECT g.img
        FROM bc_item i
        JOIN bc_item g ON g.id = i.id
                       OR coalesce(g.item_group_cd, g.item_cd) = coalesce(i.item_group_cd, i.item_cd)
        WHERE i.item_cd = @ItemCd AND octet_length(g.img) > 0
        ORDER BY g.id = i.id DESC, coalesce(g.plating, '') IN ('', 'R') DESC, g.item_cd
        LIMIT 1
        """;

    // MM-BR-822: the whole value is replaced; updated_at is kept so an open item form can still be saved
    private const string UpdateImage = "UPDATE bc_item SET img = @Image WHERE item_cd = @ItemCd";

    public Task<byte[]?> FindImageAsync(string itemCd, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.QuerySingleOrDefaultAsync<byte[]?>(
            new CommandDefinition(SelectImage, new { ItemCd = itemCd }, transaction, cancellationToken: cancellationToken));

    public async Task<bool> SaveImageAsync(string itemCd, byte[]? image, IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.ExecuteAsync(
            new CommandDefinition(UpdateImage, new { ItemCd = itemCd, Image = image }, transaction, cancellationToken: cancellationToken)) == 1;

    public async Task<ItemRow?> FindAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var row = await transaction.Connection!.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            forUpdate ? SelectByIdForUpdate : SelectById, new { Id = id }, transaction, cancellationToken: cancellationToken));
        return row?.ToItemRow();
    }

    public Task<bool> CodeExistsAsync(string itemCd, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<bool>(
            new CommandDefinition(SelectCodeExists, new { ItemCd = itemCd }, transaction, cancellationToken: cancellationToken));

    public async Task<(IReadOnlyList<ItemRow> Items, int Total)> SearchAsync(
        ItemSearch search, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var parameters = new
        {
            ItemCdPattern = string.IsNullOrWhiteSpace(search.ItemCd) ? null : EscapeLike(search.ItemCd.Trim()) + "%",
            NamePattern = string.IsNullOrWhiteSpace(search.ItemName) ? null : "%" + EscapeLike(search.ItemName.Trim()) + "%",
            ItemClass = string.IsNullOrWhiteSpace(search.ItemClass) ? null : search.ItemClass.Trim(),
            ValidFlag = string.IsNullOrWhiteSpace(search.ValidFlag) ? null : search.ValidFlag.Trim(),
            search.Limit,
            search.Offset,
        };
        var rows = await transaction.Connection!.QueryAsync<Row>(
            new CommandDefinition(Search, parameters, transaction, cancellationToken: cancellationToken));
        var total = await transaction.Connection!.ExecuteScalarAsync<int>(
            new CommandDefinition(Count, parameters, transaction, cancellationToken: cancellationToken));
        return (rows.Select(r => r.ToItemRow()).ToList(), total);
    }

    public Task<long> InsertAsync(Item item, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<long>(
            new CommandDefinition(Insert, Parameters(item, createdBy, null), transaction, cancellationToken: cancellationToken));

    public async Task<bool> UpdateAsync(
        Item item, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Update, Parameters(item, updatedBy, expectedUpdatedAt), transaction, cancellationToken: cancellationToken)) == 1;

    public Task<bool> IsTypeLockedAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<bool>(
            new CommandDefinition(SelectTypeLocked, new { Id = id }, transaction, cancellationToken: cancellationToken));

    public async Task<ItemRow?> FindAdjacentAsync(long id, bool forward, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var row = await transaction.Connection!.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            forward ? SelectNext : SelectPrevious, new { Id = id }, transaction, cancellationToken: cancellationToken));
        return row?.ToItemRow();
    }

    public Task<bool> IsInUseAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<bool>(
            new CommandDefinition(SelectInUse, new { Id = id }, transaction, cancellationToken: cancellationToken));

    public async Task<bool> DeleteAsync(long id, DateTimeOffset expectedUpdatedAt, IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Delete,
            // Npgsql only accepts UTC values for timestamptz parameters
            new { Id = id, ExpectedUpdatedAt = expectedUpdatedAt.UtcDateTime },
            transaction,
            cancellationToken: cancellationToken)) == 1;

    private static string EscapeLike(string value) => value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    private static DynamicParameters Parameters(Item item, string userId, DateTimeOffset? expectedUpdatedAt)
    {
        var f = item.Fields;
        var parameters = new DynamicParameters(new
        {
            item.Id,
            item.ItemCd,
            f.ItemName,
            f.ItemName2,
            f.ItemDesc,
            f.ItemType,
            f.Unit,
            f.ItemClass,
            f.ValidFlag,
            f.UsdPrc,
            f.SoUsdPrc,
            f.UpcCode,
            f.Location,
            f.ReorderPoint,
            f.Memo1,
            f.Memo2,
            f.Memo3,
            f.Memo4,
            f.Memo5,
            f.ItemGrade,
            f.Taxable,
            f.VatKind,
            item.Plating,
            item.ItemGroupCd,
            item.MvgCost,
            item.OnhandQty,
            item.StkVal,
            UserId = userId,
        });
        if (expectedUpdatedAt is { } expected)
        {
            parameters.Add("ExpectedUpdatedAt", expected.UtcDateTime);
        }
        return parameters;
    }

    // Npgsql reads timestamptz as a UTC DateTime; Dapper does not convert it to DateTimeOffset for constructor binding
    private sealed record Row(
        long Id,
        string ItemCd,
        string? ItemName,
        string? ItemName2,
        string? ItemDesc,
        string? ItemType,
        string? Unit,
        string? ItemClass,
        string? ValidFlag,
        decimal? UsdPrc,
        decimal? SoUsdPrc,
        string? UpcCode,
        string? Location,
        decimal? ReorderPoint,
        string? Memo1,
        string? Memo2,
        string? Memo3,
        string? Memo4,
        string? Memo5,
        string? ItemGrade,
        bool Taxable,
        string VatKind,
        string? Plating,
        string? ItemGroupCd,
        decimal? MvgCost,
        decimal? OnhandQty,
        decimal? StkVal,
        DateTime CreatedAt,
        string CreatedBy,
        DateTime UpdatedAt,
        string UpdatedBy)
    {
        public ItemRow ToItemRow() => new(
            Id, ItemCd, ItemName, ItemName2, ItemDesc, ItemType, Unit, ItemClass, ValidFlag, UsdPrc, SoUsdPrc, UpcCode, Location,
            ReorderPoint, Memo1, Memo2, Memo3, Memo4, Memo5, ItemGrade, Taxable, VatKind, Plating, ItemGroupCd, MvgCost, OnhandQty, StkVal,
            Utc(CreatedAt), CreatedBy, Utc(UpdatedAt), UpdatedBy);

        private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}
