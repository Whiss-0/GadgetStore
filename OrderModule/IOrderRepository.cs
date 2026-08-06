using api.Main;

namespace api.OrderModule
{
    public interface IOrderRepository
    {
        Task<Order?> GetByIdAsync(int id, CancellationToken ct = default);
        Task<List<Order>> GetAllAsync(CancellationToken ct = default);
        Task<List<Order>> GetByUserAsync(int userId, CancellationToken ct = default);
        Task<PaginationModel<Order>> GetPagedAsync(int pageNumber, int pageSize, CancellationToken ct = default);
        Task<int> CreateAsync(Order order, CancellationToken ct = default);
        Task<bool> UpdateAsync(Order order, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
    }
}
