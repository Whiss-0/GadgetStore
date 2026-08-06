using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using Microsoft.Data.Sqlite; // NuGet: Microsoft.Data.Sqlite

namespace api.Main
{
    public sealed class MyCon
    {
        private readonly string _connectionString;
        public int? DefaultCommandTimeoutSeconds { get; } = 30;

        private static (string server, string database, string userId, string password, string filePath, bool encrypt, bool trustCert, bool ssh, bool protectedSqlite) LoadConfiguration()
        {
            IReadOnlyDictionary<string, string> fileConfig = ConnEnvFile.LoadValues();

            return (
                GetConfigValue("DB_SERVER", fileConfig),
                GetConfigValue("DB_DATABASE", fileConfig),
                GetConfigValue("DB_USER", fileConfig),
                GetConfigValue("DB_PASSWORD", fileConfig),
                GetConfigValue("DB_FILE_PATH", fileConfig),
                ParseBool(GetConfigValue("DB_ENCRYPT", fileConfig)),
                ParseBool(GetConfigValue("DB_TRUST_CERTIFICATE", fileConfig)),
                ParseBool(GetConfigValue("DB_SSH", fileConfig)),
                ParseBool(GetConfigValue("DB_PROTECTED_SQLITE", fileConfig))
            );
        }

        private static string GetConfigValue(string key, IReadOnlyDictionary<string, string> fileConfig)
        {
            string? environmentValue = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(environmentValue))
            {
                return environmentValue;
            }

            return fileConfig.GetValueOrDefault(key, "");
        }

        private static bool ParseBool(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            return value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("1", StringComparison.OrdinalIgnoreCase)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Resolves the configured SQLite path to something that is stable in production.
        /// Absolute paths and special URI/in-memory forms are used verbatim; a relative
        /// path (e.g. "contact.db") is resolved against the application's base directory
        /// so it works regardless of the current working directory when the app runs.
        /// </summary>
        private static string ResolveDataSource(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return filePath;

            string trimmed = filePath.Trim();

            if (trimmed.Equals(":memory:", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                return trimmed;

            if (Path.IsPathRooted(trimmed))
                return trimmed;

            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, trimmed));
        }

        private static (string host, int? port) SplitHostPort(string server)
        {
            if (string.IsNullOrWhiteSpace(server)) return ("", null);
            var parts = server.Split(',', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && int.TryParse(parts[1], out var port) && port > 0)
                return (parts[0], port);
            return (server.Trim(), null);
        }

        public MyCon()
        {
            var config = LoadConfiguration();
            _ = config.ssh; // SSH tunneling is handled outside this class

            var b = new SqliteConnectionStringBuilder
            {
                DataSource = ResolveDataSource(config.filePath)
            };
            if (config.protectedSqlite)
            {
                b.Password = config.password;
            }
            _connectionString = b.ToString();
        }

        /// <summary>Returns a NEW, UNOPENED connection for the selected provider.</summary>
        public DbConnection GetConnection()
        {
            try
            {
                return new SqliteConnection(_connectionString);
            }
            catch (DbException ex)
            {
                throw new InvalidOperationException($"Database error creating connection: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Unexpected error creating connection: {ex.Message}", ex);
            }
        }

        public async Task<bool> CanConnectAsync(CancellationToken ct = default)
        {
            await using var conn = GetConnection();
            try
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);
                return conn.State == ConnectionState.Open;
            }
            catch (DbException)
            {
                return false;
            }
            finally
            {
                if (conn.State == ConnectionState.Open)
                    await conn.CloseAsync().ConfigureAwait(false);
            }
        }

        public DbCommand CreateCommand(DbConnection connection, string sql, CommandType type = CommandType.Text)
        {
            if (connection is null) throw new ArgumentNullException(nameof(connection));
            if (string.IsNullOrWhiteSpace(sql)) throw new ArgumentException("SQL is required.", nameof(sql));
            var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandType = type;
            if (DefaultCommandTimeoutSeconds.HasValue && DefaultCommandTimeoutSeconds.Value > 0)
                cmd.CommandTimeout = DefaultCommandTimeoutSeconds.Value;
            return cmd;
        }
    }
}