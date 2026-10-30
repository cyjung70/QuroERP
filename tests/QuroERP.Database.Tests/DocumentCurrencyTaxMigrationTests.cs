using System.Text.RegularExpressions;
using FluentAssertions;
using Npgsql;

namespace QuroERP.Database.Tests;

/// <summary>
/// 0400_document_currency_tax (TASK-CORE-021): the columns it adds match database-tables.md, and rows that existed before
/// it become migrated data (BC-BR-548). Real PostgreSQL; each test works in its own schema.
/// </summary>
[Collection(MigratedSchemaDefinition.Name)]
public partial class DocumentCurrencyTaxMigrationTests(MigratedSchemaFixture db)
{
    private const string Script = "0400_document_currency_tax.sql";

    private static readonly string[] PartyColumns =
        ["currency_code", "tax_code_id", "tax_party_type", "business_reg_no", "ceo_name", "business_type", "business_item"];

    private static readonly string[] HeaderColumns = ["currency_code", "exchange_rate", "supply_amt", "tax_amt", "tot_base_amt"];

    private static readonly string[] LineColumns = ["tax_code_id", "supply_amt", "tax_amt"];

    /// <summary>Columns this script owns, per table. Other documented columns belong to other tasks.</summary>
    public static TheoryData<string, string[]> OwnedColumns => new()
    {
        { "bc_customer", PartyColumns },
        { "bc_vendor", PartyColumns },
        { "mm_purchase_order", HeaderColumns },
        { "mm_goods_receipt", HeaderColumns },
        { "sd_sales_order", HeaderColumns },
        { "sd_sales_invoice", [.. HeaderColumns, "tax_exemption_cert_no"] },
        { "mm_purchase_order_line", LineColumns },
        { "mm_goods_receipt_line", [.. LineColumns, "deductible"] },
        { "sd_sales_order_line", LineColumns },
        { "sd_sales_invoice_line", [.. LineColumns, "restock"] },
        { "sd_ar_receipt", ["currency_code", "exchange_rate"] },
        { "mm_ap_payment", ["currency_code", "exchange_rate"] },
        { "sd_ar_clear", ["fx_diff_base_amt"] },
        { "sd_cm_clear", ["fx_diff_base_amt"] },
        { "mm_ap_clear", ["fx_diff_base_amt"] },
        { "bc_alternate_item_group", ["alt_item_group_name", "class"] },
        { "bc_cart_item", ["qty"] },
        { "bc_purchase_price", ["purchase_prc"] },
    };

    [DatabaseTheory, Trait("Id", "CORE-021-IT-01")]
    [MemberData(nameof(OwnedColumns))]
    public void Columns_MatchDatabaseTablesDocument(string table, string[] columns)
    {
        var documented = DocumentedColumns(table);
        using var connection = db.Open();

        foreach (var column in columns)
        {
            documented.Should().ContainKey(column, $"{table}.{column} is in database-tables.md");
            var (type, notNull) = documented[column];
            using var command = new NpgsqlCommand("""
                SELECT data_type, is_nullable FROM information_schema.columns
                WHERE table_schema = @schema AND table_name = @table AND column_name = @column
                """, connection);
            command.Parameters.AddWithValue("schema", db.Schema);
            command.Parameters.AddWithValue("table", table);
            command.Parameters.AddWithValue("column", column);
            using var reader = command.ExecuteReader();
            reader.Read().Should().BeTrue($"{table}.{column} exists");
            (reader.GetString(0), reader.GetString(1) == "NO").Should().Be((Postgres(type), notNull), $"{table}.{column}");
        }
    }

    [DatabaseFact, Trait("Id", "CORE-021-IT-02")]
    public void NoneTaxCode_IsInstalled_WithKoAndEnNames()
    {
        using var connection = db.Open();
        using var command = new NpgsqlCommand("""
            SELECT t.tax_kind || ' ' || t.rate::text || ' ' || string_agg(n.lang_code || '=' || n.name, ',' ORDER BY n.lang_code)
            FROM bc_tax_code t JOIN bc_tax_code_translation n ON n.tax_code_id = t.id
            WHERE t.tax_code = 'NONE'
            GROUP BY t.tax_kind, t.rate
            """, connection);

        command.ExecuteScalar().Should().Be("NONE 0 en=No tax,ko=세금 없음");
    }

    [DatabaseFact, Trait("Id", "CORE-021-IT-03")]
    public void ExistingDocuments_BecomeMigratedData_InTheCompanyBaseCurrency()
    {
        using var schema = new ScratchSchema();
        schema.ApplySchemaScriptsBefore(Script);
        schema.Execute("""
            INSERT INTO bc_company (name, country_code, base_currency_code, default_lang_code, created_by, updated_by)
            VALUES ('Quro', 'KR', 'KRW', 'ko', 't', 't');
            WITH po AS (INSERT INTO mm_purchase_order (po_no, tot_po_amt, created_by, updated_by)
                        VALUES ('PO1', 3000, 't', 't') RETURNING id)
            INSERT INTO mm_purchase_order_line (purchase_order_id, seq, po_amt, created_by, updated_by)
            SELECT id, s, a, 't', 't' FROM po, (VALUES (1, 1000), (2, 2000)) v(s, a);
            INSERT INTO sd_sales_invoice (dn_no, warehouse_id, created_by, updated_by) SELECT 'DN1', min(id), 't', 't' FROM bc_warehouse;
            INSERT INTO sd_ar_receipt (cls_ar_no, cls_dt, pay_method, created_by, updated_by) VALUES ('AR1', DATE '2026-10-30', 'CASH', 't', 't');
            """);

        schema.ApplySchemaScript(Script);

        schema.Rows("SELECT concat_ws(' ', currency_code, exchange_rate, supply_amt, tax_amt, tot_base_amt) FROM mm_purchase_order")
            .Should().Equal("KRW 1 3000 0 3000");
        schema.Rows("""
            SELECT concat_ws(' ', t.tax_code, l.supply_amt, l.tax_amt)
            FROM mm_purchase_order_line l JOIN bc_tax_code t ON t.id = l.tax_code_id ORDER BY l.seq
            """).Should().Equal("NONE 1000 0", "NONE 2000 0");
        schema.Rows("SELECT concat_ws(' ', currency_code, exchange_rate, supply_amt, tot_base_amt) FROM sd_sales_invoice")
            .Should().Equal("KRW 1 0 0");
        schema.Rows("SELECT concat_ws(' ', currency_code, exchange_rate) FROM sd_ar_receipt").Should().Equal("KRW 1");
    }

    [DatabaseFact, Trait("Id", "CORE-021-IT-04")]
    public void NullInAColumnThatBecomesNotNull_StopsTheScript_AndChangesNothing()
    {
        using var schema = new ScratchSchema();
        schema.ApplySchemaScriptsBefore(Script);
        schema.Execute("INSERT INTO bc_cart_item (cart_no, seq, created_by, updated_by) VALUES (1, 1, 't', 't')");

        var act = () => schema.ApplySchemaScript(Script);

        act.Should().Throw<PostgresException>().WithMessage("*bc_cart_item id=*");
        schema.Rows("SELECT column_name::text FROM information_schema.columns WHERE table_schema = current_schema() AND column_name = 'supply_amt'")
            .Should().BeEmpty();
    }

    [DatabaseFact, Trait("Id", "CORE-021-IT-05")]
    public void DocumentsWithoutACompany_StopTheScript()
    {
        using var schema = new ScratchSchema();
        schema.ApplySchemaScriptsBefore(Script);
        schema.Execute("INSERT INTO mm_purchase_order (po_no, created_by, updated_by) VALUES ('PO1', 't', 't')");

        var act = () => schema.ApplySchemaScript(Script);

        act.Should().Throw<PostgresException>().WithMessage("*bc_company*");
    }

    private static string Postgres(string documentedType) => documentedType switch
    {
        "varchar" => "character varying",
        _ => documentedType,
    };

    /// <summary>Type and NOT NULL of each column in the table's section of database-tables.md.</summary>
    private static Dictionary<string, (string Type, bool NotNull)> DocumentedColumns(string table)
    {
        var doc = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "architecture", "database-tables.md"));
        var section = Regex.Match(doc, $@"^#{{2,3}} {table}\s*$(.*?)(?=^#{{2,3}} |\z)", RegexOptions.Multiline | RegexOptions.Singleline).Groups[1].Value;
        return ColumnRow().Matches(section).ToDictionary(
            m => m.Groups[1].Value,
            m => (m.Groups[2].Value.Trim(), m.Groups[3].Value.Contains("NOT NULL", StringComparison.Ordinal)));
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "QuroErp.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("QuroErp.slnx was not found.");
    }

    [GeneratedRegex(@"^\| `([a-z0-9_]+)` \| ([^|]+)\|([^|]*)\|", RegexOptions.Multiline)]
    private static partial Regex ColumnRow();

    /// <summary>A new schema where Schema scripts are applied one by one, to run this script on existing rows.</summary>
    private sealed class ScratchSchema : IDisposable
    {
        private readonly string _name = $"t_{Guid.NewGuid():N}";
        private readonly NpgsqlConnection _connection;

        public ScratchSchema()
        {
            _connection = new NpgsqlConnection(TestDatabase.ConnectionString);
            _connection.Open();
            // The name is this class's own t_<guid>, never input
            Execute($"CREATE SCHEMA {_name}; SET search_path TO {_name}");
        }

        public void ApplySchemaScriptsBefore(string script)
        {
            foreach (var name in DatabaseMigrator.ScriptNames("Schema").TakeWhile(n => string.CompareOrdinal(n, script) < 0))
            {
                ApplySchemaScript(name);
            }
        }

        /// <summary>Runs the script in one transaction, as DbUp does.</summary>
        public void ApplySchemaScript(string name)
        {
            using var stream = typeof(DatabaseMigrator).Assembly.GetManifestResourceStream(DatabaseMigrator.ResourcePrefix("Schema") + name)!;
            using var reader = new StreamReader(stream);
            using var transaction = _connection.BeginTransaction();
            using (var command = new NpgsqlCommand(reader.ReadToEnd(), _connection, transaction))
            {
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        }

        public void Execute(string sql)
        {
            using var command = new NpgsqlCommand(sql, _connection);
            command.ExecuteNonQuery();
        }

        public List<string> Rows(string sql)
        {
            using var command = new NpgsqlCommand(sql, _connection);
            using var reader = command.ExecuteReader();
            var rows = new List<string>();
            while (reader.Read())
            {
                rows.Add(reader.GetString(0));
            }
            return rows;
        }

        public void Dispose()
        {
            Execute($"SET search_path TO public; DROP SCHEMA {_name} CASCADE");
            _connection.Dispose();
        }
    }
}
