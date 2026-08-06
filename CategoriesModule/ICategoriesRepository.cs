using api.Main;

namespace api.CategoriesModule
{
    public interface ICategoriesRepository
    {
        Task<Category?> GetByIdAsync(int id, CancellationToken ct = default);
        Task<List<Category>> GetAllAsync(CancellationToken ct = default);
        Task<PaginationModel<Category>> GetPagedAsync(int pageNumber, int pageSize, CancellationToken ct = default);
        Task<int> CreateAsync(Category category, CancellationToken ct = default);
        Task<bool> UpdateAsync(Category category, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
    }
}
