using Microsoft.Extensions.Configuration;
using Npgsql;
using Sportarr.Api.Constants;

namespace Sportarr.Api.Data;

public enum DatabaseProviderKind
{
    Sqlite,
    Postgres
}

/// <summary>
/// Parsed Postgres connection settings, shared by DI bootstrap (AddSportarrDatabase)
/// and BackupService (pg_dump/pg_restore) so the two never drift. SQLite's connection
/// string is still built separately from the existing data-path pipeline - this class
/// only exists to carry the Postgres side of the picture.
/// </summary>
public class DatabaseSettings
{
    public DatabaseProviderKind Provider { get; init; } = DatabaseProviderKind.Sqlite;
    public string? Host { get; init; }
    public int Port { get; init; } = 5432;
    public string? Name { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }

    /// <summary>Raw connection string escape hatch. Wins over Host/Port/Name/Username/Password when set.</summary>
    public string? ConnectionString { get; init; }

    public static DatabaseSettings FromConfiguration(IConfiguration config)
    {
        var providerRaw = config[ConfigurationKeys.DatabaseProvider];
        var provider = string.Equals(providerRaw, "postgres", StringComparison.OrdinalIgnoreCase)
            ? DatabaseProviderKind.Postgres
            : DatabaseProviderKind.Sqlite;

        var port = 5432;
        var portRaw = config[ConfigurationKeys.DatabasePort];
        if (!string.IsNullOrWhiteSpace(portRaw) && int.TryParse(portRaw, out var parsedPort) && parsedPort > 0)
        {
            port = parsedPort;
        }

        return new DatabaseSettings
        {
            Provider = provider,
            Host = config[ConfigurationKeys.DatabaseHost],
            Port = port,
            Name = config[ConfigurationKeys.DatabaseName],
            Username = config[ConfigurationKeys.DatabaseUsername],
            Password = config[ConfigurationKeys.DatabasePassword],
            ConnectionString = config[ConfigurationKeys.DatabaseConnectionString],
        };
    }

    /// <summary>Build a Postgres connection string from the discrete Host/Port/Name/Username/Password parts.</summary>
    public string BuildNpgsqlConnectionString()
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Host,
            Port = Port,
            Database = Name,
            Username = Username,
            Password = Password,
        };
        return builder.ConnectionString;
    }

    /// <summary>The connection string to hand to UseNpgsql: the raw escape hatch if set, otherwise built from parts.</summary>
    public string ResolvePostgresConnectionString() =>
        !string.IsNullOrWhiteSpace(ConnectionString) ? ConnectionString : BuildNpgsqlConnectionString();
}
