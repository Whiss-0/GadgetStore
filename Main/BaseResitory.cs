#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite; // NuGet: Microsoft.Data.Sqlite

namespace api.Main
{
    /// <summary>
    /// Lightweight base repository (no EF). Works with any DbConnection returned by api.Main.MyCon.
    /// </summary>
    public abstract class BaseRepository
    {
        protected readonly MyCon _db;

        protected BaseRepository(MyCon dbConnection)
        {
            if (dbConnection is null)
                throw new ArgumentNullException(nameof(dbConnection));

            _db = dbConnection;
        }

        protected async Task<T?> ExecuteScalarAsync<T>(
            string sql,
            IEnumerable<DbParameter>? parameters = null,
            CommandType commandType = CommandType.Text,
            int? commandTimeoutSeconds = null,
            DbTransaction? transaction = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(sql))
                throw new ArgumentException("SQL is required.", nameof(sql));

            await using var connection = _db.GetConnection();
            await using var command = PrepareCommand(connection, sql, commandType, commandTimeoutSeconds, transaction, parameters);

            await connection.OpenAsync(ct).ConfigureAwait(false);
            var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return SafeChangeType<T>(result);
        }

        protected async Task<int> ExecuteNonQueryAsync(
            string sql,
            IEnumerable<DbParameter>? parameters = null,
            CommandType commandType = CommandType.Text,
            int? commandTimeoutSeconds = null,
            DbTransaction? transaction = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(sql))
                throw new ArgumentException("SQL is required.", nameof(sql));

            await using var connection = _db.GetConnection();
            await using var command = PrepareCommand(connection, sql, commandType, commandTimeoutSeconds, transaction, parameters);

            await connection.OpenAsync(ct).ConfigureAwait(false);
            return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        protected async Task<List<T>> ExecuteReaderToListAsync<T>(
            string sql,
            Func<DbDataReader, T> mapper,
            IEnumerable<DbParameter>? parameters = null,
            CommandType commandType = CommandType.Text,
            int? commandTimeoutSeconds = null,
            DbTransaction? transaction = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(sql))
                throw new ArgumentException("SQL is required.", nameof(sql));
            if (mapper is null)
                throw new ArgumentNullException(nameof(mapper));

            var list = new List<T>();

            await using var connection = _db.GetConnection();
            await using var command = PrepareCommand(connection, sql, commandType, commandTimeoutSeconds, transaction, parameters);

            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.CloseConnection, ct)
                                                  .ConfigureAwait(false);

            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                list.Add(mapper(reader));

            return list;
        }

        /// <summary>
        /// Runs <paramref name="work"/> against a single open connection and transaction,
        /// committing on success and rolling back on any exception. Every statement issued
        /// inside <paramref name="work"/> must use the supplied connection/transaction
        /// (via the transactional execute overloads below) to participate atomically.
        /// </summary>
        protected async Task<T> WithTransactionAsync<T>(
            Func<DbConnection, DbTransaction, Task<T>> work,
            IsolationLevel isolation = IsolationLevel.ReadCommitted,
            CancellationToken ct = default)
        {
            if (work is null)
                throw new ArgumentNullException(nameof(work));

            await using var connection = _db.GetConnection();
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using var tx = await connection.BeginTransactionAsync(isolation, ct).ConfigureAwait(false);
            try
            {
                var result = await work(connection, tx).ConfigureAwait(false);
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return result;
            }
            catch
            {
                await tx.RollbackAsync(ct).ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>Transactional overload: runs on the supplied open connection/transaction.</summary>
        protected static async Task<T?> ExecuteScalarAsync<T>(
            DbConnection connection,
            DbTransaction transaction,
            string sql,
            IEnumerable<DbParameter>? parameters = null,
            CommandType commandType = CommandType.Text,
            int? commandTimeoutSeconds = null,
            CancellationToken ct = default)
        {
            if (connection is null)
                throw new ArgumentNullException(nameof(connection));
            if (string.IsNullOrWhiteSpace(sql))
                throw new ArgumentException("SQL is required.", nameof(sql));

            await using var command = PrepareCommand(connection, sql, commandType, commandTimeoutSeconds, transaction, parameters);
            var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return SafeChangeType<T>(result);
        }

        /// <summary>Transactional overload: runs on the supplied open connection/transaction.</summary>
        protected static async Task<int> ExecuteNonQueryAsync(
            DbConnection connection,
            DbTransaction transaction,
            string sql,
            IEnumerable<DbParameter>? parameters = null,
            CommandType commandType = CommandType.Text,
            int? commandTimeoutSeconds = null,
            CancellationToken ct = default)
        {
            if (connection is null)
                throw new ArgumentNullException(nameof(connection));
            if (string.IsNullOrWhiteSpace(sql))
                throw new ArgumentException("SQL is required.", nameof(sql));

            await using var command = PrepareCommand(connection, sql, commandType, commandTimeoutSeconds, transaction, parameters);
            return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        /// <summary>Transactional overload: runs on the supplied open connection/transaction.</summary>
        protected static async Task<List<T>> ExecuteReaderToListAsync<T>(
            DbConnection connection,
            DbTransaction transaction,
            string sql,
            Func<DbDataReader, T> mapper,
            IEnumerable<DbParameter>? parameters = null,
            CommandType commandType = CommandType.Text,
            int? commandTimeoutSeconds = null,
            CancellationToken ct = default)
        {
            if (connection is null)
                throw new ArgumentNullException(nameof(connection));
            if (string.IsNullOrWhiteSpace(sql))
                throw new ArgumentException("SQL is required.", nameof(sql));
            if (mapper is null)
                throw new ArgumentNullException(nameof(mapper));

            var list = new List<T>();

            await using var command = PrepareCommand(connection, sql, commandType, commandTimeoutSeconds, transaction, parameters);
            // Default behavior (NOT CloseConnection): the connection is owned by WithTransactionAsync.
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                list.Add(mapper(reader));

            return list;
        }

        /// <summary>
        /// Creates a provider-native parameter directly, without allocating a throwaway
        /// connection/command per call.
        /// </summary>
        protected static DbParameter CreateParameter(
            string name,
            object? value,
            DbType? dbType = null,
            int? size = null,
            ParameterDirection direction = ParameterDirection.Input)
        {
            var p = new SqliteParameter(name, value ?? DBNull.Value);
            p.Direction = direction;
            if (dbType.HasValue) p.DbType = dbType.Value;
            if (size.HasValue && size.Value > 0) p.Size = size.Value;

            return p;
        }

        /// <summary>
        /// Reads a column value from the reader, coercing it to <typeparamref name="T"/>,
        /// returning <paramref name="defaultValue"/> when the column is NULL.
        /// </summary>
        protected static T ReadValue<T>(DbDataReader reader, string columnName, T defaultValue)
        {
            int ordinal = reader.GetOrdinal(columnName);
            if (reader.IsDBNull(ordinal))
            {
                return defaultValue;
            }

            object value = reader.GetValue(ordinal);
            if (value is T typedValue)
            {
                return typedValue;
            }

            Type targetType = typeof(T);
            if (targetType == typeof(string))
            {
                return (T)(object)(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
            }

            if (targetType == typeof(int))
            {
                return (T)(object)Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }

            if (targetType == typeof(long))
            {
                return (T)(object)Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }

            if (targetType == typeof(short))
            {
                return (T)(object)Convert.ToInt16(value, CultureInfo.InvariantCulture);
            }

            if (targetType == typeof(byte))
            {
                return (T)(object)Convert.ToByte(value, CultureInfo.InvariantCulture);
            }

            if (targetType == typeof(bool))
            {
                return (T)(object)Convert.ToBoolean(value, CultureInfo.InvariantCulture);
            }

            if (targetType == typeof(decimal))
            {
                // SQLite may store numeric columns as TEXT (e.g. empty string inserted via a bad row).
                // Convert.ToDecimal throws FormatException on "" — use TryParse as a safe fallback.
                if (value is string strDecimal)
                {
                    return decimal.TryParse(strDecimal, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal parsed)
                        ? (T)(object)parsed
                        : (T)(object)defaultValue;
                }
                return (T)(object)Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            }

            if (targetType == typeof(double))
            {
                return (T)(object)Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }

            if (targetType == typeof(float))
            {
                return (T)(object)Convert.ToSingle(value, CultureInfo.InvariantCulture);
            }

            if (targetType == typeof(DateTime))
            {
                return (T)(object)Convert.ToDateTime(value, CultureInfo.InvariantCulture);
            }

            if (targetType == typeof(Guid))
            {
                if (value is Guid guidValue)
                {
                    return (T)(object)guidValue;
                }

                return (T)(object)Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
            }

            if (targetType == typeof(byte[]))
            {
                if (value is byte[] bytes)
                {
                    return (T)(object)bytes;
                }

                throw new InvalidCastException($"Cannot convert column '{columnName}' value type '{value.GetType().FullName}' to byte[].");
            }

            return (T)Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }

        // -------- internals --------
        private static DbCommand PrepareCommand(
            DbConnection connection,
            string sql,
            CommandType commandType,
            int? commandTimeoutSeconds,
            DbTransaction? transaction,
            IEnumerable<DbParameter>? parameters)
        {
            var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandType = commandType;

            if (transaction != null)
                command.Transaction = transaction;

            if (commandTimeoutSeconds.HasValue && commandTimeoutSeconds.Value > 0)
                command.CommandTimeout = commandTimeoutSeconds.Value;

            if (parameters != null)
            {
                foreach (var p in parameters)
                {
                    // Clone into this command to avoid reuse issues
                    var clone = command.CreateParameter();
                    clone.ParameterName = p.ParameterName;
                    clone.Value = p.Value;
                    clone.Direction = p.Direction;
                    clone.DbType = p.DbType;
                    clone.Size = p.Size;
                    command.Parameters.Add(clone);
                }
            }

            return command;
        }

        private static T? SafeChangeType<T>(object? value)
        {
            if (value is null || value is DBNull) return default;
            if (value is T t) return t;

            var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            return (T)Convert.ChangeType(value, targetType);
        }
    }
}