using DbUp.Engine;
using DbUp.Engine.Output;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace QuroERP.Database.Tests;

/// <summary>
/// 통합 테스트용 DB. 연결 문자열: 환경변수 ConnectionStrings__QuroErpTest > user-secrets (ConnectionStrings:QuroErpTest).
/// 테스트 실행마다 새 스키마를 만들어 마이그레이션을 적용하고, 끝나면 그 스키마를 지운다.
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

public sealed class MigratedSchemaFixture : IDisposable
{
    public MigratedSchemaFixture()
    {
        if (string.IsNullOrWhiteSpace(TestDatabase.ConnectionString))
        {
            return;
        }

        Schema = $"t_{Guid.NewGuid():N}";
        ConnectionString = new NpgsqlConnectionStringBuilder(TestDatabase.ConnectionString) { SearchPath = Schema }.ToString();
        Execute($"CREATE SCHEMA {Schema}");
        FirstRun = DatabaseMigrator.Upgrade(ConnectionString, includeDemo: false, Schema, new NoOpUpgradeLog());
    }

    public string Schema { get; } = "";

    public string ConnectionString { get; } = "";

    public DatabaseUpgradeResult? FirstRun { get; }

    public NpgsqlConnection Open()
    {
        var connection = new NpgsqlConnection(ConnectionString);
        connection.Open();
        return connection;
    }

    public void Dispose()
    {
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
public sealed class MigratedSchemaDefinition : ICollectionFixture<MigratedSchemaFixture>
{
    public const string Name = "MigratedSchema";
}
