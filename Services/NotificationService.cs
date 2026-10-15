using api.NotificationModule;
using api.OrderModule;

namespace api.Services;

public sealed class NotificationService : INotificationService
{
    private readonly INotificationRepository _repository;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(INotificationRepository repository, ILogger<NotificationService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task CreateOrderStatusNotificationAsync(Order order, string previousStatus, CancellationToken ct = default)
    {
        if (string.Equals(previousStatus, order.status, StringComparison.Ordinal)) return;
        var content = order.status switch
        {
            "Processing" => ("OrderProcessing", "Your order is being prepared", $"Order #{order.order_id} is now being prepared by our store team."),
            "Shipped" => ("OrderShipped", "Your order has shipped", $"Order #{order.order_id} is on the way."),
            "Delivered" => ("OrderDelivered", "Your order was delivered", $"Order #{order.order_id} has been marked as delivered."),
            "Cancelled" => ("OrderCancelled", "Your order was cancelled", $"Order #{order.order_id} has been cancelled."),
            _ => (string.Empty, string.Empty, string.Empty),
        };
        if (string.IsNullOrEmpty(content.Item1)) return;
        try
        {
            await _repository.CreateForUserAsync(new Notification
            {
                recipient_user_id = order.user_id,
                notification_type = content.Item1,
                title = content.Item2,
                message = content.Item3,
                related_order_id = order.order_id,
                created_at = DateTime.UtcNow,
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Notification write failed for order {OrderId} status {Status}", order.order_id, order.status);
        }
    }
}
