using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace api.DTOs
{
    public class RegisterRequest
    {
        [Required]
        [StringLength(100)]
        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        [Required]
        [MinLength(6)]
        [JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("role_id")]
        public int RoleId { get; set; } = 3; // defaults to standard user; admin can override
    }
}