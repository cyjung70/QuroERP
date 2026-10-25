using DbUp.Engine.Output;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using QuroERP.Database;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Common.Auth;

/// <summary>
/// 임시 스키마(t_&lt;guid&gt;)에 QuroERP.Database 마이그레이션(Schema·Seed)을 적용하고 AddInfrastructure로 서비스를 구성한다.
/// 끝나면 스키마를 지운다.
/// </summary>
public sealed class AuthSchemaFixture : IDisposable
{
    public AuthSchemaFixture()
    {
        if (string.IsNullOrWhiteSpace(TestDatabase.ConnectionString))
        {
            return;
        }

        Schema = $"t_{Guid.NewGuid():N}";
        ConnectionString = new NpgsqlConnectionStringBuilder(TestDatabase.ConnectionString) { SearchPath = Schema }.ToString();
        Execute($"CREATE SCHEMA {Schema}");
        var result = DatabaseMigrator.Upgrade(ConnectionString, includeDemo: false, Schema, new NoOpUpgradeLog());
        if (!result.Successful)
        {
            throw new InvalidOperationException($"마이그레이션 실패: {result.ErrorScript?.Name}", result.Error);
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{DependencyInjection.ConnectionStringName}"] = ConnectionString,
            })
            .Build();
        Services = new ServiceCollection().AddInfrastructure(configuration).BuildServiceProvider();
    }

    public string Schema { get; } = "";

    public string ConnectionString { get; } = "";

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
public sealed class AuthSchemaDefinition : ICollectionFixture<AuthSchemaFixture>
{
    public const string Name = "AuthSchema";
}
