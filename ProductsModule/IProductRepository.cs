using api.Main;

namespace api.ProductsModule
{
    public interface IProductRepository
    {
        Task<Product?> GetByIdAsync(int id, CancellationToken ct = default);
        Task<List<Product>> GetAllAsync(CancellationToken ct = default);
        Task<PaginationModel<Product>> GetPagedAsync(int pageNumber, int pageSize, CancellationToken ct = default);
        Task<List<Product>> GetByCategoryAsync(int categoryId, CancellationToken ct = default);
        Task<int> CreateAsync(Product product, CancellationToken ct = default);
        Task<bool> UpdateAsync(Product product, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
    }
}
