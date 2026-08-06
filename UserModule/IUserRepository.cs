using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using api.Main;

namespace api.UserModule
{
    public interface IUserRepository
    {
        Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default);
        Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
        Task<User?> GetByIdAsync(int id, CancellationToken ct = default);
        Task<List<User>> GetAllAsync(CancellationToken ct = default);
        Task<PaginationModel<User>> GetPagedAsync(int pageNumber, int pageSize, CancellationToken ct = default);
        Task<int> CreateAsync(User user, CancellationToken ct = default);
        Task<bool> UpdateAsync(User user, CancellationToken ct = default);
        Task<bool> UpdatePasswordAsync(int userId, string newPasswordHash, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
    }
}
