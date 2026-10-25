using Npgsql;

namespace MiroClone.CanvasService.Tests;

/// <summary>
/// Gives this service its own database on the server named by TEST_POSTGRES, as in the estate (one database per service,
/// ADR 0003). Creates it when missing, so a shared history table never mixes two services' migrations.
/// </summary>
internal static class PostgreSqlTestDatabase
{
    /// <summary>The migration test's database. Tests that run concurrently each name their own, so DDL never races.</summary>
    public const string DatabaseName = "canvas_test";

    public static async Task<string> PrepareAsync(
        string serverConnectionString,
        CancellationToken cancellationToken,
        string databaseName = DatabaseName)
    {
        var admin = new NpgsqlConnectionStringBuilder(serverConnectionString) { Database = "postgres" };
        await using var connection = new NpgsqlConnection(admin.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using (var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", connection))
        {
            exists.Parameters.AddWithValue("name", databaseName);
            if (await exists.ExecuteScalarAsync(cancellationToken) is null)
            {
                await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
                await create.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        return new NpgsqlConnectionStringBuilder(serverConnectionString) { Database = databaseName }.ConnectionString;
    }
}
