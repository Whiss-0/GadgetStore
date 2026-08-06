using api.Main;

namespace api.UserRoleModule
{
    public interface IUserRoleRespository
    {
        Task<UserRole?> GetByIdAsync(int id, CancellationToken ct = default);
        Task<List<UserRole>> GetAllAsync(CancellationToken ct = default);
        Task<PaginationModel<UserRole>> GetPagedAsync(int pageNumber, int pageSize, CancellationToken ct = default);
        Task<int> CreateAsync(UserRole userRole, CancellationToken ct = default);
        Task<bool> UpdateAsync(UserRole userRole, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
    }
}
