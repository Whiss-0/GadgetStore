using api.OrderModule;

namespace api.Services;

public interface INotificationService
{
    Task CreateOrderStatusNotificationAsync(Order order, string previousStatus, CancellationToken ct = default);
}
