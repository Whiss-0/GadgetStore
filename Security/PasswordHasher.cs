using System;
using System.Security.Cryptography;
using System.Text;
using Isopoh.Cryptography.Argon2;
using Isopoh.Cryptography.SecureArray;

namespace api.Security
{
    public static class PasswordHasher
    {
        // ===== ARGON2 PARAMETERS =====
        private const int SaltSize = 16; // 128-bit salt
        private const int HashSize = 32; // 256-bit hash
        private const int MemorySize = 65536; // 64 MB
        private const int Iterations = 4; // Time cost
        private const int Parallelism = 2; // CPU threads

        // =========================================================
        // HASH PASSWORD (REGISTER / CHANGE PASSWORD)
        // =========================================================
        public static string Hash(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Password is required.");

            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] passwordBytes = Encoding.UTF8.GetBytes(password);

            var config = new Argon2Config
            {
                Type = Argon2Type.HybridAddressing,
                Version = Argon2Version.Nineteen,
                Password = passwordBytes,
                Salt = salt,
                MemoryCost = MemorySize,
                TimeCost = Iterations,
                Lanes = Parallelism,
                Threads = Parallelism,
                HashLength = HashSize
            };

            using (var argon2A = new Argon2(config))
            {
                using (SecureArray<byte> hashA = argon2A.Hash())
                {
                    byte[] hash = hashA.Buffer;

                    // Storage format:
                    // base64(salt).base64(hash)
                    return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
                }
            }
        }

        // =========================================================
        // VERIFY PASSWORD (LOGIN)
        // =========================================================
        public static bool Verify(string storedHash, string password)
        {
            if (string.IsNullOrWhiteSpace(storedHash) ||
                string.IsNullOrWhiteSpace(password))
                return false;

            var parts = storedHash.Split('.');
            if (parts.Length != 2)
                return false;

            byte[] salt;
            byte[] expectedHash;

            try
            {
                salt = Convert.FromBase64String(parts[0]);
                expectedHash = Convert.FromBase64String(parts[1]);
            }
            catch
            {
                // Corrupted or invalid hash format
                return false;
            }

            byte[] passwordBytes = Encoding.UTF8.GetBytes(password);

            var config = new Argon2Config
            {
                Type = Argon2Type.HybridAddressing,
                Version = Argon2Version.Nineteen,
                Password = passwordBytes,
                Salt = salt,
                MemoryCost = MemorySize,
                TimeCost = Iterations,
                Lanes = Parallelism,
                Threads = Parallelism,
                HashLength = HashSize
            };

            using (var argon2A = new Argon2(config))
            {
                using (SecureArray<byte> hashA = argon2A.Hash())
                {
                    byte[] actualHash = hashA.Buffer;

                    // Constant-time comparison
                    return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
                }
            }
        }
    }
}