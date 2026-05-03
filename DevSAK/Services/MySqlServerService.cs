using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevSAK.Models;
using MySqlConnector;

namespace DevSAK.Services
{
    public class MySqlServerService
    {
        public string BuildConnectionString(MySqlConnectionProfile profile, string? database = null)
        {
            var builder = new MySqlConnectionStringBuilder
            {
                Server = profile.Host,
                Port = (uint)profile.Port,
                UserID = profile.UserName,
                Password = profile.Password,
                Database = database ?? profile.DefaultDatabase ?? string.Empty,
                SslMode = profile.UseSsl ? MySqlSslMode.Required : MySqlSslMode.Disabled,
                ConnectionTimeout = 8,
                DefaultCommandTimeout = 30,
                AllowUserVariables = true
            };

            return builder.ConnectionString;
        }

        public async Task<MySqlConnectionTestResult> TestConnectionAsync(MySqlConnectionProfile profile, CancellationToken cancellationToken)
        {
            await using var connection = new MySqlConnection(BuildConnectionString(profile));
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new MySqlCommand("SELECT @@hostname, @@version;", connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            string serverHost = profile.Host;
            string serverVersion = "Desconhecida";

            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                serverHost = reader.IsDBNull(0) ? profile.Host : reader.GetString(0);
                serverVersion = reader.IsDBNull(1) ? "Desconhecida" : reader.GetString(1);
            }

            return new MySqlConnectionTestResult(serverHost, serverVersion);
        }

        public async Task<IReadOnlyList<string>> ListDatabasesAsync(MySqlConnectionProfile profile, CancellationToken cancellationToken)
        {
            var databases = new List<string>();

            await using var connection = new MySqlConnection(BuildConnectionString(profile));
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new MySqlCommand("SHOW DATABASES;", connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!reader.IsDBNull(0))
                {
                    databases.Add(reader.GetString(0));
                }
            }

            databases.Sort(StringComparer.OrdinalIgnoreCase);
            return databases;
        }

        public async Task<int> EstimateTableCountAsync(MySqlConnectionProfile profile, string database, CancellationToken cancellationToken)
        {
            await using var connection = new MySqlConnection(BuildConnectionString(profile, "information_schema"));
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            const string sql = """
                SELECT COUNT(*)
                FROM information_schema.tables
                WHERE table_schema = @databaseName
                  AND table_type = 'BASE TABLE';
                """;

            await using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@databaseName", database);

            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result is null || result == DBNull.Value ? 0 : Convert.ToInt32(result);
        }

        public async Task<IReadOnlyList<MySqlTableBackupEstimate>> GetTableBackupEstimatesAsync(
            MySqlConnectionProfile profile,
            string database,
            CancellationToken cancellationToken)
        {
            var tables = new List<MySqlTableBackupEstimate>();

            await using var connection = new MySqlConnection(BuildConnectionString(profile, "information_schema"));
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            const string sql = """
                SELECT
                    table_name,
                    COALESCE(data_length, 0),
                    COALESCE(index_length, 0),
                    COALESCE(table_rows, 0)
                FROM information_schema.tables
                WHERE table_schema = @databaseName
                  AND table_type = 'BASE TABLE'
                ORDER BY table_name;
                """;

            await using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@databaseName", database);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (reader.IsDBNull(0))
                {
                    continue;
                }

                var tableName = reader.GetString(0);
                var dataLength = reader.IsDBNull(1) ? 0L : reader.GetInt64(1);
                var indexLength = reader.IsDBNull(2) ? 0L : reader.GetInt64(2);
                var rowCount = reader.IsDBNull(3) ? 0L : reader.GetInt64(3);

                var estimatedBytes = Math.Max(1L, dataLength + indexLength);
                if (estimatedBytes == 1L && rowCount > 0)
                {
                    estimatedBytes = Math.Max(1L, rowCount * 128L);
                }

                tables.Add(new MySqlTableBackupEstimate(tableName, estimatedBytes, rowCount));
            }

            return tables;
        }

        public async Task CreateDatabaseAsync(MySqlConnectionProfile profile, string databaseName, CancellationToken cancellationToken)
        {
            await using var connection = new MySqlConnection(BuildConnectionString(profile, string.Empty));
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var safeName = databaseName.Replace("`", "``");
            await using var command = new MySqlCommand(
                $"CREATE DATABASE IF NOT EXISTS `{safeName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;",
                connection);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task DropDatabaseIfExistsAsync(MySqlConnectionProfile profile, string databaseName, CancellationToken cancellationToken)
        {
            await using var connection = new MySqlConnection(BuildConnectionString(profile, string.Empty));
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var safeName = databaseName.Replace("`", "``");
            await using var command = new MySqlCommand($"DROP DATABASE IF EXISTS `{safeName}`;", connection);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public sealed record MySqlConnectionTestResult(string ServerHost, string ServerVersion);

    public sealed record MySqlTableBackupEstimate(string TableName, long EstimatedBytes, long RowCount);
}
