using System.Reflection;
using Microsoft.Extensions.Configuration;
using QuroERP.Database;

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
var result = DatabaseMigrator.Upgrade(connectionString, includeDemo);
if (!result.Successful)
{
    Console.Error.WriteLine($"실패한 스크립트: {result.ErrorScript?.Name}");
    Console.Error.WriteLine(result.Error);
    return 1;
}

return 0;
