using System.Data.Common;
using api.Main;

namespace api.UserRoleModule
{
    public class UserRoleRespository : BaseRepository, IUserRoleRespository
    {
        public UserRoleRespository(MyCon dbConnection) : base(dbConnection) { }

        public async Task<UserRole?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            const string sql = "SELECT Role_ID, Role_Name FROM UserRoles WHERE Role_ID = @id LIMIT 1;";
            var parameters = new[] { CreateParameter("@id", id) };
            var list = await ExecuteReaderToListAsync(sql, MapUserRole, parameters, ct: ct);
            return list.Count > 0 ? list[0] : null;
        }

        public async Task<List<UserRole>> GetAllAsync(CancellationToken ct = default)
        {
            const string sql = "SELECT Role_ID, Role_Name FROM UserRoles;";
            return await ExecuteReaderToListAsync(sql, MapUserRole, ct: ct);
        }

        public async Task<PaginationModel<UserRole>> GetPagedAsync(int pageNumber, int pageSize, CancellationToken ct = default)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 10;
            int offset = (pageNumber - 1) * pageSize;

            var totalCountScalar = await ExecuteScalarAsync<long>("SELECT COUNT(*) FROM UserRoles;", ct: ct);
            int totalCount = Convert.ToInt32(totalCountScalar);

            const string sql = "SELECT Role_ID, Role_Name FROM UserRoles LIMIT @pageSize OFFSET @offset;";
            var parameters = new[] { CreateParameter("@pageSize", pageSize), CreateParameter("@offset", offset) };
            var items = await ExecuteReaderToListAsync(sql, MapUserRole, parameters, ct: ct);

            return new PaginationModel<UserRole>
            {
                Items = items,
                TotalCount = totalCount,
                PageSize = pageSize,
                CurrentPage = pageNumber
            };
        }

        public async Task<int> CreateAsync(UserRole userRole, CancellationToken ct = default)
        {
            if (userRole is null) throw new ArgumentNullException(nameof(userRole));
            const string sql = @"
                INSERT INTO UserRoles (Role_Name) VALUES (@Role_Name);
                SELECT last_insert_rowid();";
            var parameters = new[] { CreateParameter("@Role_Name", userRole.Role_Name) };
            var newIdScalar = await ExecuteScalarAsync<long>(sql, parameters, ct: ct);
            int newId = Convert.ToInt32(newIdScalar);
            userRole.Role_ID = newId;
            return newId;
        }

        public async Task<bool> UpdateAsync(UserRole userRole, CancellationToken ct = default)
        {
            if (userRole is null) throw new ArgumentNullException(nameof(userRole));
            const string sql = "UPDATE UserRoles SET Role_Name = @Role_Name WHERE Role_ID = @id;";
            var parameters = new[] { CreateParameter("@Role_Name", userRole.Role_Name), CreateParameter("@id", userRole.Role_ID) };
            int rows = await ExecuteNonQueryAsync(sql, parameters, ct: ct);
            return rows > 0;
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            const string sql = "DELETE FROM UserRoles WHERE Role_ID = @id;";
            var parameters = new[] { CreateParameter("@id", id) };
            int rows = await ExecuteNonQueryAsync(sql, parameters, ct: ct);
            return rows > 0;
        }

        private static UserRole MapUserRole(DbDataReader reader) => new UserRole
        {
            Role_ID = ReadValue(reader, "Role_ID", 0),
            Role_Name = ReadValue(reader, "Role_Name", string.Empty)
        };
    }
}
