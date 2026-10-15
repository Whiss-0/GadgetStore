namespace api.NotificationModule;

public sealed class Notification
{
    public int notification_id { get; set; }
    public int recipient_user_id { get; set; }
    public int? sender_user_id { get; set; }
    public string notification_type { get; set; } = string.Empty;
    public string title { get; set; } = string.Empty;
    public string message { get; set; } = string.Empty;
    public int? related_order_id { get; set; }
    public bool is_read { get; set; }
    public DateTime created_at { get; set; }
    public DateTime? read_at { get; set; }
}

public sealed class NotificationDraft
{
    public int? Sender_User_ID { get; init; }
    public string Notification_Type { get; init; } = "AdminAnnouncement";
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public int? Related_Order_ID { get; init; }
}
