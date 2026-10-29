namespace QuroErp.Domain.Common.Codes;

/// <summary>A business table column that stores Minor codes of a Major (codes.md "업무 테이블에 저장하는 값").</summary>
public sealed record CodeColumn(string Table, string Column);

/// <summary>
/// SD-BR-914: the columns that store each Major's Minor codes. A Minor code stored in any of them is in use and cannot be deleted.
/// Add a column here when a new table stores a code.
/// </summary>
public static class CodeUsages
{
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<CodeColumn>> Columns = new Dictionary<string, IReadOnlyList<CodeColumn>>
    {
        ["AP_PAYMENT_METHOD"] = [new("mm_ap_payment", "pay_method")],
        ["AR_PAYMENT_METHOD"] = [new("sd_ar_receipt", "pay_method")],
        ["DISCOUNT_STATUS"] = [new("bc_customer", "dc_status")],
        ["FREIGHT_CHARGE"] =
        [
            new("bc_customer", "freight_charge"), new("sd_sales_order", "freight_charge"), new("sd_sales_invoice", "freight_charge"),
        ],
        [CodeMajors.Grade] = [new("bc_item", "item_grade")],
        [CodeMajors.ItemClass] = [new("bc_item", "class"), new("bc_alternate_item_group", "class")],
        [CodeMajors.ItemType] = [new("bc_item", "type")],
        [CodeMajors.MovementType] = [new("mm_goods_movement", "mov_type")],
        [CodeMajors.Plating] = [new("bc_item", "plating")],
        ["PO_STATUS"] = [new("mm_purchase_order", "status")],
        ["SALES_REP"] = [new("bc_customer", "rep"), new("sd_sales_order", "rep"), new("sd_sales_invoice", "rep")],
        ["SHIPMENT_VIA"] = [new("sd_sales_order", "ship_via"), new("sd_sales_invoice", "ship_via"), new("sd_shipment", "ship_via")],
        ["SO_STATUS"] = [new("sd_sales_order", "status")],
        ["STATE"] =
        [
            new("bc_customer", "state"), new("bc_customer", "ship_state"), new("bc_vendor", "state"), new("bc_vendor", "ship_state"),
            new("sd_sales_order", "ship_state"), new("sd_sales_invoice", "ship_state"), new("sd_shipment", "ship_state"),
            new("bc_tax_area", "state_code"), new("sd_customer_tax_exemption", "state_code"),
        ],
        ["TERMS"] =
        [
            new("bc_customer", "terms"), new("bc_vendor", "terms"), new("mm_goods_receipt", "terms"), new("sd_sales_order", "terms"),
            new("sd_sales_invoice", "terms"),
        ],
        [CodeMajors.Unit] =
        [
            new("bc_item", "unit"), new("mm_goods_issue_line", "gi_unit"), new("mm_goods_return_line", "ret_unit"),
            new("sd_mrp_part", "item_unit"), new("sd_mrp_product", "item_unit"), new("sd_production_result_line", "prod_unit"),
        ],
        [CodeMajors.ValidFlag] =
        [
            new("sys_app_user", "valid_flag"), new("sys_menu", "valid_flag"), new("bc_item", "valid_flag"), new("bc_customer", "valid_flag"),
            new("bc_vendor", "valid_flag"),
        ],
    };

    /// <summary>The columns of majorCode; none when no table stores its codes yet.</summary>
    public static IReadOnlyList<CodeColumn> Of(string majorCode) => Columns.TryGetValue(majorCode, out var columns) ? columns : [];
}
