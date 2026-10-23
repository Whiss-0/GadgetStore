using api.OrderModule;

namespace api.Services;

public interface INotificationService
{
    Task CreateNewCustomerOrderNotificationAsync(int orderId, CancellationToken ct = default);
    Task CreateOrderStatusNotificationAsync(Order order, string previousStatus, CancellationToken ct = default);
    Task CreateTrackingUpdateNotificationAsync(Order order, CancellationToken ct = default);
}
