using System.Reflection;
using DbUp;
using DbUp.Engine;
using DbUp.Support;
using Microsoft.Extensions.Configuration;

// QuroERP DB 마이그레이션 (DbUp)
//   기본:   Scripts/Schema → Scripts/Seed 순서로 아직 적용하지 않은 스크립트를 적용한다 (기록: schemaversions)
//   --demo: 이어서 Scripts/Demo도 적용한다 (기록: schemaversions_demo)
// 연결 문자열 우선순위: --connection 인자 > 환경변수 ConnectionStrings__QuroErp > dotnet user-secrets

const string ConnectionKey = "ConnectionStrings:QuroErp";

var configuration = new ConfigurationBuilder()
    .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args, new Dictionary<string, string> { ["--connection"] = ConnectionKey })
    .Build();

var connectionString = configuration[ConnectionKey];
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine($"연결 문자열이 없습니다. --connection 인자, 환경변수 ConnectionStrings__QuroErp, user-secrets '{ConnectionKey}' 중 하나를 설정하세요.");
    return 2;
}

var includeDemo = args.Contains("--demo", StringComparer.OrdinalIgnoreCase);
var assembly = Assembly.GetExecutingAssembly();

var core = Upgrade(connectionString, "schemaversions",
    ("Schema", 1),
    ("Seed", 2));
if (!core.Successful)
{
    return 1;
}

if (includeDemo)
{
    var demo = Upgrade(connectionString, "schemaversions_demo", ("Demo", 1));
    if (!demo.Successful)
    {
        return 1;
    }
}

return 0;

DatabaseUpgradeResult Upgrade(string connection, string journalTable, params (string Folder, int Order)[] folders)
{
    var builder = DeployChanges.To
        .PostgresqlDatabase(connection)
        .JournalToPostgresqlTable("public", journalTable)
        .WithTransactionPerScript()
        .LogToConsole();

    foreach (var (folder, order) in folders)
    {
        var prefix = $"{assembly.GetName().Name}.Scripts.{folder}.";
        builder = builder.WithScriptsEmbeddedInAssembly(
            assembly,
            name => name.StartsWith(prefix, StringComparison.Ordinal),
            new SqlScriptOptions { ScriptType = ScriptType.RunOnce, RunGroupOrder = order });
    }

    var result = builder.Build().PerformUpgrade();
    if (!result.Successful)
    {
        Console.Error.WriteLine($"실패한 스크립트: {result.ErrorScript?.Name}");
        Console.Error.WriteLine(result.Error);
    }
    return result;
}
