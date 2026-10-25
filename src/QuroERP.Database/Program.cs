using System.Reflection;
using Microsoft.Extensions.Configuration;
using QuroERP.Database;

// QuroERP DB 마이그레이션 (DbUp)
//   기본:   Scripts/Schema → Scripts/Seed 순서로 아직 적용하지 않은 스크립트를 적용한다 (기록: schemaversions)
//   --demo: 이어서 Scripts/Demo도 적용한다 (기록: schemaversions_demo)
//   --create-admin <userid>: 마이그레이션 대신 관리자 계정을 만들거나 비밀번호를 초기화한다.
//                            비밀번호는 콘솔에서 입력받는다 (화면에 보이지 않는다)
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

var adminUserId = configuration["create-admin"];
if (adminUserId is not null)
{
    return await CreateAdminAsync(connectionString, adminUserId);
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

static async Task<int> CreateAdminAsync(string connectionString, string userId)
{
    if (string.IsNullOrWhiteSpace(userId))
    {
        Console.Error.WriteLine("사용법: --create-admin <userid>");
        return 2;
    }

    var password = ReadPassword("비밀번호: ");
    if (password.Length == 0 || password != ReadPassword("비밀번호 확인: "))
    {
        Console.Error.WriteLine("비밀번호가 비어 있거나 확인 값과 다릅니다.");
        return 2;
    }

    try
    {
        var created = await AdminAccount.CreateOrResetAsync(connectionString, userId, password, CancellationToken.None);
        Console.WriteLine(created
            ? $"관리자 '{userId}'를 만들었습니다. 첫 로그인 때 비밀번호를 바꿔야 합니다."
            : $"'{userId}'의 비밀번호를 초기화했습니다. 다음 로그인 때 비밀번호를 바꿔야 합니다.");
        return 0;
    }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

// 입력을 화면에 보이지 않게 읽는다. 입력이 리디렉션되면 한 줄을 읽는다
static string ReadPassword(string prompt)
{
    Console.Write(prompt);
    if (Console.IsInputRedirected)
    {
        var line = Console.ReadLine() ?? "";
        Console.WriteLine();
        return line;
    }

    var buffer = new System.Text.StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
        {
            break;
        }
        if (key.Key == ConsoleKey.Backspace)
        {
            if (buffer.Length > 0)
            {
                buffer.Length--;
            }
            continue;
        }
        if (!char.IsControl(key.KeyChar))
        {
            buffer.Append(key.KeyChar);
        }
    }
    Console.WriteLine();
    return buffer.ToString();
}
