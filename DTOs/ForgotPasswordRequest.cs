using System.ComponentModel.DataAnnotations;

namespace api.DTOs
{
    public class ForgotPasswordRequest
    {
        [Required]
        public string Username { get; set; } = string.Empty;
    }
}