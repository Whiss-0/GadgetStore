using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using api.UserModule;

namespace api.Security
{
    public interface IJwtTokenService
    {
        string GenerateToken(User user);
        string GeneratePasswordResetToken(string userId);
        bool TryValidatePasswordResetToken(string token, out string userId);
    }

    public class JwtTokenService : IJwtTokenService
    {
        private const string PasswordResetTokenType = "password_reset";
        private readonly IConfiguration _configuration;
        private readonly string _secretKey;
        private readonly string _issuer;
        private readonly string _audience;
        private readonly int _expirationMinutes;
        private readonly int _resetExpirationMinutes;

        public JwtTokenService(IConfiguration configuration, IHostEnvironment environment)
        {
            _configuration = configuration;
            _secretKey = JwtConfiguration.ResolveSigningKey(_configuration, environment);
            _issuer = _configuration["Jwt:Issuer"] ?? JwtConfiguration.DefaultIssuer;
            _audience = _configuration["Jwt:Audience"] ?? JwtConfiguration.DefaultAudience;
            _expirationMinutes = int.TryParse(_configuration["Jwt:ExpirationMinutes"], out int exp) ? exp : JwtConfiguration.DefaultAccessExpirationMinutes;
            _resetExpirationMinutes = int.TryParse(_configuration["Jwt:ResetExpirationMinutes"], out int resetExp) ? resetExp : JwtConfiguration.DefaultResetExpirationMinutes;
        }

        public string GenerateToken(User user)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_secretKey);

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.User_ID.ToString()),
                new Claim(ClaimTypes.Name, user.Name),
                new Claim("user_role_id", (user.Role_ID ?? 0).ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddMinutes(_expirationMinutes),
                Issuer = _issuer,
                Audience = _audience,
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(key),
                    SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        public string GeneratePasswordResetToken(string userId)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_secretKey);
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim("token_type", PasswordResetTokenType),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddMinutes(_resetExpirationMinutes),
                Issuer = _issuer,
                Audience = _audience,
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(key),
                    SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        public bool TryValidatePasswordResetToken(string token, out string userId)
        {
            userId = string.Empty;
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var key = Encoding.UTF8.GetBytes(_secretKey);

                var principal = tokenHandler.ValidateToken(
                    token,
                    new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = _issuer,
                        ValidAudience = _audience,
                        IssuerSigningKey = new SymmetricSecurityKey(key),
                        ClockSkew = TimeSpan.FromMinutes(1)
                    },
                    out _);

                string tokenType = principal.FindFirst("token_type")?.Value ?? string.Empty;
                if (!string.Equals(tokenType, PasswordResetTokenType, StringComparison.Ordinal))
                {
                    return false;
                }

                userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
                return !string.IsNullOrWhiteSpace(userId);
            }
            catch
            {
                return false;
            }
        }
    }
}