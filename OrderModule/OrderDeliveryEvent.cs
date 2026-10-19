namespace api.OrderModule;

public sealed class OrderDeliveryEvent
{
    public long delivery_event_id { get; set; }
    public string status { get; set; } = string.Empty;
    public string event_type { get; set; } = string.Empty;
    public string? note { get; set; }
    public DateTime created_at { get; set; }
    public string actor_role { get; set; } = "System";
}
