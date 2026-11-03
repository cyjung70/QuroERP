using Npgsql;

namespace QuroERP.Database.Tests;

/// <summary>A new schema where Schema scripts are applied one by one, to run a script on existing rows.</summary>
internal sealed class ScratchSchema : IDisposable
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
