using api.Main;

namespace api.ReviewModule
{
    public interface IReviewRepository
    {
        Task<Review?> GetByIdAsync(int id, CancellationToken ct = default);
        Task<List<Review>> GetByProductAsync(int productId, CancellationToken ct = default);
        Task<List<Review>> GetByUserAsync(int userId, CancellationToken ct = default);
        Task<List<Review>> GetAllAsync(CancellationToken ct = default);
        Task<int> CreateAsync(Review review, CancellationToken ct = default);
        Task<bool> UpdateAsync(Review review, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
    }
}
