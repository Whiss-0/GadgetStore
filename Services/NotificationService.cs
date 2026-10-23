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

    public async Task CreateNewCustomerOrderNotificationAsync(int orderId, CancellationToken ct = default)
    {
        try
        {
            var recipients = (await _repository.GetUserIdsByRoleAsync(1, ct))
                .Concat(await _repository.GetUserIdsByRoleAsync(2, ct))
                .Distinct()
                .ToList();
            if (recipients.Count == 0) return;

            int sent = await _repository.CreateForUsersAsync(recipients, new NotificationDraft
            {
                Notification_Type = "NewCustomerOrder",
                Title = "New customer order",
                Message = $"Customer order #{orderId} was placed. Open the order queue to review it.",
                Related_Order_ID = orderId,
            }, ct);
            _logger.LogInformation("Created new-order notifications for {RecipientCount} staff/admin recipients (order {OrderId})", sent, orderId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "New-order notification broadcast failed for order {OrderId}", orderId);
        }
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

    public async Task CreateTrackingUpdateNotificationAsync(Order order, CancellationToken ct = default)
    {
        string details = string.Join(" ", new[]
        {
            string.IsNullOrWhiteSpace(order.courier_name) ? null : $"Courier: {order.courier_name}.",
            string.IsNullOrWhiteSpace(order.tracking_number) ? null : $"Tracking number: {order.tracking_number}.",
            string.IsNullOrWhiteSpace(order.estimated_delivery_date) ? null : $"Estimated delivery: {order.estimated_delivery_date}."
        }.Where(value => value != null));
        string message = $"Tracking details for order #{order.order_id} have been updated." +
            (details.Length == 0 ? string.Empty : " " + details);

        try
        {
            await _repository.CreateForUserAsync(new Notification
            {
                recipient_user_id = order.user_id,
                notification_type = "SystemNotice",
                title = "Tracking details updated",
                message = message,
                related_order_id = order.order_id,
                created_at = DateTime.UtcNow,
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tracking notification write failed for order {OrderId}", order.order_id);
        }
    }
}
