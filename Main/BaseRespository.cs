using System;
using System.Collections.Generic;
using System.IO;

namespace api.Main
{
    public static class ConnEnvFile
    {
        public static IReadOnlyDictionary<string, string> LoadValues()
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string? configFile = ResolvePath();

            if (string.IsNullOrWhiteSpace(configFile) || !File.Exists(configFile))
            {
                return values;
            }

            foreach (string line in File.ReadAllLines(configFile))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] parts = line.Split('=', 2);
                if (parts.Length != 2)
                {
                    continue;
                }

                string key = parts[0].Trim();
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                values[key] = parts[1].Trim();
            }

            return values;
        }

        public static IDictionary<string, string?> LoadConfigurationValues()
        {
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            foreach ((string key, string value) in LoadValues())
            {
                if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
                {
                    continue;
                }

                string normalizedKey = key.Replace("__", ":", StringComparison.Ordinal);
                values[normalizedKey] = value;
            }

            return values;
        }

        public static string? ResolvePath()
        {
            string[] candidatePaths =
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "conn.env"),
                Path.Combine(Directory.GetCurrentDirectory(), "conn.env"),
                Path.Combine(Directory.GetCurrentDirectory(), "..", "conn.env")
            };

            foreach (string path in candidatePaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }
    }
}