namespace api.DTOs
{
    public class UpdateUserRequest
    {
        public string Username { get; set; } = string.Empty;
        public int UserRoleId { get; set; }
        public string? Password { get; set; } // Optional - only update if provided
        public string? Address { get; set; }
        public string? Region { get; set; }
        public string? Province { get; set; }
        public string? City_Municipality { get; set; }
        public string? Barangay { get; set; }
    }
}