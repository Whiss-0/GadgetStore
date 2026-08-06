namespace api.DTOs
{
    public class UpdateUserRequest
    {
        public string Username { get; set; } = string.Empty;
        public int UserRoleId { get; set; }
        public int OsId { get; set; }
        public string? Password { get; set; } // Optional - only update if provided
    }
}