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
        [Range(1, int.MaxValue)]
        [JsonPropertyName("role_id")]
        public int RoleId { get; set; }

        [JsonPropertyName("office_id")]
        public int? OfficeId { get; set; }

        [JsonPropertyName("os_id")]
        public int? OsId { get; set; }
    }
}