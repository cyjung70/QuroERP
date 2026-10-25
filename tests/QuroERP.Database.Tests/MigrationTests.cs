using System.Text.RegularExpressions;
using DbUp.Engine.Output;
using FluentAssertions;
using Npgsql;

namespace QuroERP.Database.Tests;

/// <summary>빈 스키마에 마이그레이션을 적용해 테이블·인덱스·기초자료를 확인한다 (실제 PostgreSQL).</summary>
[Collection(MigratedSchemaDefinition.Name)]
public partial class MigrationTests(MigratedSchemaFixture db)
{
    [DatabaseFact, Trait("Id", "DB-IT-01")]
    public void Upgrade_EmptySchema_AppliesAllScripts()
    {
        db.FirstRun!.Successful.Should().BeTrue(db.FirstRun.Error?.Message);
        db.FirstRun.Scripts.Should().HaveCount(DatabaseMigrator.ScriptNames("Schema").Count + DatabaseMigrator.ScriptNames("Seed").Count);
    }

    [DatabaseFact, Trait("Id", "DB-IT-02")]
    public void Upgrade_SecondRun_AppliesNothing()
    {
        var second = DatabaseMigrator.Upgrade(db.ConnectionString, includeDemo: false, db.Schema, new NoOpUpgradeLog());

        second.Successful.Should().BeTrue();
        second.Scripts.Should().BeEmpty();
    }

    [DatabaseFact, Trait("Id", "DB-IT-03")]
    public void Tables_MatchDatabaseTablesDocument()
    {
        var tables = Query<string>("""
            SELECT table_name FROM information_schema.tables
            WHERE table_schema = @schema AND table_type = 'BASE TABLE' AND table_name <> @journal
            """, ("journal", DatabaseMigrator.JournalTable));

        tables.Should().BeEquivalentTo(DocumentedTables());
    }

    [DatabaseFact, Trait("Id", "DB-IT-04")]
    public void ForeignKeys_AllHaveIndex()
    {
        // 외래 키 컬럼이 어떤 인덱스의 첫 컬럼도 아니면 인덱스가 없는 것이다 (quro-dapper 체크리스트)
        var missing = Query<string>("""
            SELECT c.conrelid::regclass::text || '.' || a.attname
            FROM pg_constraint c
            JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = c.conkey[1]
            WHERE c.contype = 'f' AND c.connamespace = @schema::regnamespace
              AND NOT EXISTS (SELECT 1 FROM pg_index i WHERE i.indrelid = c.conrelid AND i.indkey[0] = c.conkey[1])
            """);

        missing.Should().BeEmpty();
    }

    [DatabaseFact, Trait("Id", "DB-IT-05")]
    public void Seed_EveryCodeHasKoAndEnTranslation()
    {
        var majorsWithout = Query<string>("""
            SELECT m.major_code FROM sys_code_major m
            WHERE (SELECT count(DISTINCT lang_code) FROM sys_code_major_translation t
                   WHERE t.major_id = m.id AND t.lang_code IN ('ko', 'en')) < 2
            """);
        var minorsWithout = Query<string>("""
            SELECT m.major_code || '.' || n.minor_code FROM sys_code_minor n JOIN sys_code_major m ON m.id = n.major_id
            WHERE (SELECT count(DISTINCT lang_code) FROM sys_code_minor_translation t
                   WHERE t.minor_id = n.id AND t.lang_code IN ('ko', 'en')) < 2
            """);

        Query<long>("SELECT count(*) FROM sys_code_major").Single().Should().Be(24);
        Query<long>("SELECT count(*) FROM sys_code_minor").Single().Should().Be(121);
        majorsWithout.Should().BeEmpty();
        minorsWithout.Should().BeEmpty();
    }

    [DatabaseFact, Trait("Id", "DB-IT-06")]
    public void Seed_ContainsStatusCodesUsedByDomain()
    {
        // codes.md 예시 SalesOrderStatus Entry/Released/Closed = SO/RE/CL
        Query<string>("""
            SELECT n.minor_code FROM sys_code_minor n JOIN sys_code_major m ON m.id = n.major_id
            WHERE m.major_code = 'SO_STATUS' ORDER BY n.sort_order
            """).Should().Equal("SO", "RE", "CL");
        Query<string>("SELECT role_code FROM sys_role ORDER BY role_code")
            .Should().Equal("admin", "manager", "poweruser", "salesman", "srep");
    }

    [DatabaseFact, Trait("Id", "DB-IT-07")]
    public void Columns_HaveNoSizeOrPrecision()
    {
        // database.md "타입": 문자는 varchar, 소수는 numeric으로 크기를 정하지 않는다
        var sized = Query<string>("""
            SELECT table_name || '.' || column_name FROM information_schema.columns
            WHERE table_schema = @schema AND table_name <> @journal
              AND (character_maximum_length IS NOT NULL OR (data_type = 'numeric' AND numeric_precision IS NOT NULL)
                   OR data_type IN ('character', 'timestamp without time zone'))
            """, ("journal", DatabaseMigrator.JournalTable));

        sized.Should().BeEmpty();
    }

    /// <summary>database-tables.md의 "목록"과 "새 테이블" 표에 있는 테이블 이름.</summary>
    private static IEnumerable<string> DocumentedTables()
    {
        var doc = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "architecture", "database-tables.md"));
        var listed = ListedTable().Matches(doc).Select(m => m.Groups[1].Value);
        var added = NewTableRow().Matches(doc).Select(m => m.Groups[1].Value);
        return listed.Concat(added).Distinct();
    }

    private List<T> Query<T>(string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = db.Open();
        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("schema", db.Schema);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }
        using var reader = command.ExecuteReader();
        var rows = new List<T>();
        while (reader.Read())
        {
            rows.Add(reader.GetFieldValue<T>(0));
        }
        return rows;
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "QuroErp.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("QuroErp.slnx를 찾지 못했다.");
    }

    [GeneratedRegex(@"^- \[`(\w+)`\]", RegexOptions.Multiline)]
    private static partial Regex ListedTable();

    [GeneratedRegex(@"^\| `(\w+)` \| [^|]+ \| (?:BC|SD|MM) \|", RegexOptions.Multiline)]
    private static partial Regex NewTableRow();
}
