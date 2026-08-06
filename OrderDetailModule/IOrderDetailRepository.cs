using api.Main;

namespace api.OrderDetailModule
{
    public interface IOrderDetailRepository
    {
        Task<OrderDetail?> GetByIdAsync(int id, CancellationToken ct = default);
        Task<List<OrderDetail>> GetByOrderAsync(int orderId, CancellationToken ct = default);
        Task<List<OrderDetail>> GetAllAsync(CancellationToken ct = default);
        Task<int> CreateAsync(OrderDetail detail, CancellationToken ct = default);
        Task<bool> UpdateAsync(OrderDetail detail, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
    }
}
