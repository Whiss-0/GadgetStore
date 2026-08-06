using api.Main;

namespace api.WishlistModule
{
    public interface IWishlistRepository
    {
        Task<Wishlist?> GetByIdAsync(int id, CancellationToken ct = default);
        Task<List<Wishlist>> GetByUserAsync(int userId, CancellationToken ct = default);
        Task<Wishlist?> GetByUserAndProductAsync(int userId, int productId, CancellationToken ct = default);
        Task<int> CreateAsync(Wishlist wishlist, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
        Task<bool> ClearWishlistAsync(int userId, CancellationToken ct = default);
    }
}
