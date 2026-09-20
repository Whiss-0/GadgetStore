namespace api.ActivityModule;

public sealed class ActivityLog
{
    public long Activity_ID { get; set; }
    public int? User_ID { get; set; }
    public int? Actor_User_ID { get; set; }
    public string Actor_Role { get; set; } = "System";
    public string Activity_Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? Related_Order_ID { get; set; }
    public int? Related_Product_ID { get; set; }
    public DateTime Created_At { get; set; }
}
