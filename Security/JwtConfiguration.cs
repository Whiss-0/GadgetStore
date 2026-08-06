using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace api.Security
{
    public static class JwtConfiguration
    {
        public const int MinJwtKeyLength = 32;
        public const string DefaultIssuer = "gadgetSystem-api";
        public const string DefaultAudience = "gadgetSystem-client";
        public const int DefaultAccessExpirationMinutes = 60;
        public const int DefaultResetExpirationMinutes = 15;
        public const string DevelopmentFallbackKey = "LocalDevelopmentJwtKey-ReplaceBeforeProduction-2026";

        public static string ResolveSigningKey(IConfiguration configuration, IHostEnvironment environment)
        {
            string? configuredKey = configuration["Jwt:Key"];
            if (string.IsNullOrWhiteSpace(configuredKey))
            {
                if (environment.IsDevelopment())
                {
                    return DevelopmentFallbackKey;
                }

                throw new InvalidOperationException(
                    $"Jwt:Key is required in non-development environments. Configure a value that is at least {MinJwtKeyLength} characters.");
            }

            if (configuredKey.Length < MinJwtKeyLength)
            {
                throw new InvalidOperationException($"Jwt:Key must be at least {MinJwtKeyLength} characters.");
            }

            if (!environment.IsDevelopment() && string.Equals(configuredKey, DevelopmentFallbackKey, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The development fallback JWT key cannot be used outside Development.");
            }

            return configuredKey;
        }
    }
}