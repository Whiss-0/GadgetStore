using System.Text.Json.Serialization;

namespace api.ActivityModule;

public sealed class ActivityLog
{
    [JsonPropertyName("Activity_ID")]
    public long Activity_ID { get; set; }

    [JsonPropertyName("User_ID")]
    public int? User_ID { get; set; }

    [JsonPropertyName("Actor_User_ID")]
    public int? Actor_User_ID { get; set; }

    [JsonPropertyName("Actor_Role")]
    public string Actor_Role { get; set; } = "System";

    [JsonPropertyName("Activity_Type")]
    public string Activity_Type { get; set; } = string.Empty;

    [JsonPropertyName("Description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("Related_Order_ID")]
    public int? Related_Order_ID { get; set; }

    [JsonPropertyName("Related_Product_ID")]
    public int? Related_Product_ID { get; set; }

    [JsonPropertyName("Created_At")]
    public DateTime Created_At { get; set; }
}