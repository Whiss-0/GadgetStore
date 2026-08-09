using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace api.Security
{
    public enum OtpVerifyResult { Valid, Invalid, Expired, MaxAttemptsReached }

    public interface IOtpService
    {
        Task<string> GenerateAsync(int userId, string purpose = "reset", CancellationToken ct = default);
        Task<OtpVerifyResult> VerifyAsync(int userId, string code, string purpose = "reset", CancellationToken ct = default);
    }

    public class OtpService : IOtpService
    {
        private readonly string _connectionString;
        private static readonly TimeSpan Expiry = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);
        private const int MaxAttempts = 5;

        public OtpService(IConfiguration config)
        {
            _connectionString = config["ConnectionStrings:Default"]
                ?? throw new InvalidOperationException("Database connection string not configured.");
        }

        private static string HashOtp(string code) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant();

        public async Task<string> GenerateAsync(int userId, string purpose = "reset", CancellationToken ct = default)
        {
            string code = RandomNumberGenerator.GetInt32(100_000, 999_999).ToString();
            string hash = HashOtp(code);
            var now = DateTime.UtcNow;

            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);

            // Invalidate any previous unused OTPs for this user + purpose
            await using (var inv = conn.CreateCommand())
            {
                inv.CommandText = "UPDATE otp SET verified = 1 WHERE user_id = @uid AND purpose = @purpose AND verified = 0";
                inv.Parameters.AddWithValue("@uid", userId);
                inv.Parameters.AddWithValue("@purpose", purpose);
                await inv.ExecuteNonQueryAsync(ct);
            }

            await using var insert = conn.CreateCommand();
            insert.CommandText = """
                INSERT INTO otp (user_id, otp_hash, purpose, expires_at, verified, attempt_count, created_at)
                VALUES (@uid, @hash, @purpose, @expires, 0, 0, @created)
                """;
            insert.Parameters.AddWithValue("@uid", userId);
            insert.Parameters.AddWithValue("@hash", hash);
            insert.Parameters.AddWithValue("@purpose", purpose);
            insert.Parameters.AddWithValue("@expires", now.Add(Expiry).ToString("o"));
            insert.Parameters.AddWithValue("@created", now.ToString("o"));
            await insert.ExecuteNonQueryAsync(ct);

            return code;
        }

        public async Task<OtpVerifyResult> VerifyAsync(int userId, string code, string purpose = "reset", CancellationToken ct = default)
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);

            await using var select = conn.CreateCommand();
            select.CommandText = """
                SELECT otp_id, otp_hash, expires_at, attempt_count, locked_until
                FROM otp WHERE user_id = @uid AND purpose = @purpose AND verified = 0
                ORDER BY otp_id DESC LIMIT 1
                """;
            select.Parameters.AddWithValue("@uid", userId);
            select.Parameters.AddWithValue("@purpose", purpose);

            int otpId; string storedHash; DateTime expiresAt; int attempts; string? lockedUntilStr;
            await using (var reader = await select.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct)) return OtpVerifyResult.Invalid;
                otpId = reader.GetInt32(0);
                storedHash = reader.GetString(1);
                expiresAt = DateTime.Parse(reader.GetString(2));
                attempts = reader.GetInt32(3);
                lockedUntilStr = reader.IsDBNull(4) ? null : reader.GetString(4);
            }

            var now = DateTime.UtcNow;
            if (lockedUntilStr != null && now < DateTime.Parse(lockedUntilStr))
                return OtpVerifyResult.MaxAttemptsReached;
            if (now > expiresAt) return OtpVerifyResult.Expired;
            if (attempts >= MaxAttempts) return OtpVerifyResult.MaxAttemptsReached;

            attempts++;
            object lockUntilValue = attempts >= MaxAttempts ? now.Add(LockDuration).ToString("o") : DBNull.Value;

            await using (var incr = conn.CreateCommand())
            {
                incr.CommandText = "UPDATE otp SET attempt_count = @att, locked_until = @lock WHERE otp_id = @id";
                incr.Parameters.AddWithValue("@att", attempts);
                incr.Parameters.AddWithValue("@lock", lockUntilValue);
                incr.Parameters.AddWithValue("@id", otpId);
                await incr.ExecuteNonQueryAsync(ct);
            }

            if (!string.Equals(storedHash, HashOtp(code), StringComparison.OrdinalIgnoreCase))
                return OtpVerifyResult.Invalid;

            await using var markUsed = conn.CreateCommand();
            markUsed.CommandText = "UPDATE otp SET verified = 1 WHERE otp_id = @id";
            markUsed.Parameters.AddWithValue("@id", otpId);
            await markUsed.ExecuteNonQueryAsync(ct);

            return OtpVerifyResult.Valid;
        }
    }
}
