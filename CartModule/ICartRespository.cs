using api.Main;

namespace api.CartModule
{
    public interface ICartRespository
    {
        Task<Cart?> GetByIdAsync(int id, CancellationToken ct = default);
        Task<List<Cart>> GetByUserAsync(int userId, CancellationToken ct = default);
        Task<Cart?> GetByUserAndProductAsync(int userId, int productId, CancellationToken ct = default);
        Task<int> CreateAsync(Cart cart, CancellationToken ct = default);
        Task<bool> UpdateQuantityAsync(int cartId, int quantity, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
        Task<bool> ClearCartAsync(int userId, CancellationToken ct = default);
    }
}
