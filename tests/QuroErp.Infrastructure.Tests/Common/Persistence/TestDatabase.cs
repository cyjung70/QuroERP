using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace QuroErp.Infrastructure.Tests.Common.Persistence;

/// <summary>
/// 통합 테스트용 DB. 연결 문자열: 환경변수 ConnectionStrings__QuroErpTest > user-secrets (ConnectionStrings:QuroErpTest).
/// </summary>
public static class TestDatabase
{
    public static string? ConnectionString { get; } = new ConfigurationBuilder()
        .AddUserSecrets(typeof(TestDatabase).Assembly, optional: true)
        .AddEnvironmentVariables()
        .Build()["ConnectionStrings:QuroErpTest"];

    public const string SkipReason = "통합 테스트 DB 연결 문자열(ConnectionStrings:QuroErpTest)이 없다.";
}

/// <summary>연결 문자열이 없으면 건너뛰는 DB 통합 테스트.</summary>
public sealed class DatabaseFactAttribute : FactAttribute
{
    public DatabaseFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(TestDatabase.ConnectionString))
        {
            Skip = TestDatabase.SkipReason;
        }
    }
}

/// <summary>
/// 테스트 실행마다 임시 스키마(t_&lt;guid&gt;)와 확인용 테이블을 만들고, AddInfrastructure로 서비스를 구성한다.
/// 끝나면 스키마를 지운다.
/// </summary>
public sealed class InfrastructureFixture : IDisposable
{
    public InfrastructureFixture()
    {
        if (string.IsNullOrWhiteSpace(TestDatabase.ConnectionString))
        {
            return;
        }

        Schema = $"t_{Guid.NewGuid():N}";
        Execute($"CREATE SCHEMA {Schema}");
        Execute($"CREATE TABLE {Schema}.uow_probe (id integer PRIMARY KEY, probe_name varchar NOT NULL)");

        var connectionString = new NpgsqlConnectionStringBuilder(TestDatabase.ConnectionString) { SearchPath = Schema }.ToString();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{DependencyInjection.ConnectionStringName}"] = connectionString,
            })
            .Build();
        Services = new ServiceCollection().AddInfrastructure(configuration).BuildServiceProvider();
    }

    public string Schema { get; } = "";

    public ServiceProvider? Services { get; }

    public T Get<T>() where T : notnull => Services!.GetRequiredService<T>();

    public void Dispose()
    {
        Services?.Dispose();
        if (Schema.Length > 0)
        {
            Execute($"DROP SCHEMA {Schema} CASCADE");
        }
    }

    // Schema는 이 클래스가 만든 t_<guid> 값만 쓴다 (외부 입력 아님)
    private static void Execute(string sql)
    {
        using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }
}

[CollectionDefinition(Name)]
public sealed class InfrastructureDefinition : ICollectionFixture<InfrastructureFixture>
{
    public const string Name = "Infrastructure";
}
