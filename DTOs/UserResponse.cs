namespace api.DTOs
{
    public class UserResponse
    {
        public int User_ID { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Address { get; set; }
        public int? Role_ID { get; set; }
    }
}