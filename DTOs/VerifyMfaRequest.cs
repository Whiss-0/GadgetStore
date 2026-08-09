using System.ComponentModel.DataAnnotations;

namespace api.DTOs
{
    public class VerifyMfaRequest
    {
        [Required]
        public string Username { get; set; } = string.Empty;

        [Required]
        [StringLength(6, MinimumLength = 6)]
        public string Code { get; set; } = string.Empty;
    }
}
