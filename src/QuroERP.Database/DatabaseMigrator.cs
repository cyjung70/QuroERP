using System.Reflection;
using DbUp;
using DbUp.Engine;
using DbUp.Engine.Output;
using DbUp.Support;

namespace QuroERP.Database;

/// <summary>
/// Scripts/Schema → Scripts/Seed를 적용하고, 요청하면 Scripts/Demo도 적용한다 (database.md "마이그레이션").
/// </summary>
public static class DatabaseMigrator
{
    public const string JournalTable = "schemaversions";
    public const string DemoJournalTable = "schemaversions_demo";

    private static readonly Assembly ScriptAssembly = typeof(DatabaseMigrator).Assembly;

    /// <summary>스크립트 리소스 이름 앞부분. 예: QuroERP.Database.Scripts.Schema.</summary>
    public static string ResourcePrefix(string folder) => $"{ScriptAssembly.GetName().Name}.Scripts.{folder}.";

    /// <summary>실행 파일에 포함된 스크립트 파일 이름 (폴더별, 이름 순서).</summary>
    public static IReadOnlyList<string> ScriptNames(string folder)
    {
        var prefix = ResourcePrefix(folder);
        return ScriptAssembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
            .Select(name => name[prefix.Length..])
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <param name="schema">적용 기록 테이블을 둘 스키마. 스크립트의 테이블은 연결의 search_path에 만들어진다.</param>
    public static DatabaseUpgradeResult Upgrade(string connectionString, bool includeDemo, string schema = "public", IUpgradeLog? log = null)
    {
        var core = Run(connectionString, schema, JournalTable, log, ("Schema", 1), ("Seed", 2));
        if (!core.Successful || !includeDemo)
        {
            return core;
        }
        return Run(connectionString, schema, DemoJournalTable, log, ("Demo", 1));
    }

    private static DatabaseUpgradeResult Run(
        string connectionString, string schema, string journalTable, IUpgradeLog? log, params (string Folder, int Order)[] folders)
    {
        var builder = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .JournalToPostgresqlTable(schema, journalTable)
            .WithTransactionPerScript();
        builder = log is null ? builder.LogToConsole() : builder.LogTo(log);

        foreach (var (folder, order) in folders)
        {
            var prefix = ResourcePrefix(folder);
            builder = builder.WithScriptsEmbeddedInAssembly(
                ScriptAssembly,
                name => name.StartsWith(prefix, StringComparison.Ordinal),
                new SqlScriptOptions { ScriptType = ScriptType.RunOnce, RunGroupOrder = order });
        }

        return builder.Build().PerformUpgrade();
    }
}
