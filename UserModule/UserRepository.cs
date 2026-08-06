using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using api.Main;

namespace api.UserModule
{
    public class UserRepository : BaseRepository, IUserRepository
    {
        public UserRepository(MyCon dbConnection) : base(dbConnection)
        {
        }

        // ── Auth: look up by name column ──────────────────────────────────────
        public async Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default)
        {
            const string sql = @"
                SELECT user_ID, name, email, password, address, Role_ID
                FROM users
                WHERE name = @name LIMIT 1;";

            var parameters = new[] { CreateParameter("@name", username) };
            var list = await ExecuteReaderToListAsync(sql, MapUser, parameters, ct: ct);
            return list.Count > 0 ? list[0] : null;
        }

        // ── Look up by email ──────────────────────────────────────────────────
        public async Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)
        {
            const string sql = @"
                SELECT user_ID, name, email, password, address, Role_ID
                FROM users
                WHERE email = @email LIMIT 1;";

            var parameters = new[] { CreateParameter("@email", email) };
            var list = await ExecuteReaderToListAsync(sql, MapUser, parameters, ct: ct);
            return list.Count > 0 ? list[0] : null;
        }

        // ── Get by ID ─────────────────────────────────────────────────────────
        public async Task<User?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            const string sql = @"
                SELECT user_ID, name, email, password, address, Role_ID
                FROM users
                WHERE user_ID = @user_ID LIMIT 1;";

            var parameters = new[] { CreateParameter("@user_ID", id) };
            var list = await ExecuteReaderToListAsync(sql, MapUser, parameters, ct: ct);
            return list.Count > 0 ? list[0] : null;
        }

        // ── Get all ───────────────────────────────────────────────────────────
        public async Task<List<User>> GetAllAsync(CancellationToken ct = default)
        {
            const string sql = @"
                SELECT user_ID, name, email, password, address, Role_ID
                FROM users;";

            return await ExecuteReaderToListAsync(sql, MapUser, ct: ct);
        }

        // ── Paginated list ────────────────────────────────────────────────────
        public async Task<PaginationModel<User>> GetPagedAsync(int pageNumber, int pageSize, CancellationToken ct = default)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize  < 1) pageSize  = 10;
            int offset = (pageNumber - 1) * pageSize;

            var totalCountScalar = await ExecuteScalarAsync<long>("SELECT COUNT(*) FROM users;", ct: ct);
            int totalCount = Convert.ToInt32(totalCountScalar);

            const string dataSql = @"
                SELECT user_ID, name, email, password, address, Role_ID
                FROM users
                LIMIT @pageSize OFFSET @offset;";

            var parameters = new[]
            {
                CreateParameter("@pageSize", pageSize),
                CreateParameter("@offset",   offset)
            };

            var items = await ExecuteReaderToListAsync(dataSql, MapUser, parameters, ct: ct);

            return new PaginationModel<User>
            {
                Items       = items,
                TotalCount  = totalCount,
                PageSize    = pageSize,
                CurrentPage = pageNumber
            };
        }

        // ── Create ────────────────────────────────────────────────────────────
        public async Task<int> CreateAsync(User user, CancellationToken ct = default)
        {
            if (user is null) throw new ArgumentNullException(nameof(user));

            const string sql = @"
                INSERT INTO users (name, email, password, address, Role_ID)
                VALUES (@name, @email, @password, @address, @Role_ID);
                SELECT last_insert_rowid();";

            var parameters = new[]
            {
                CreateParameter("@name",     user.Name),
                CreateParameter("@email",    (object?)user.Email    ?? DBNull.Value),
                CreateParameter("@password", user.Password),
                CreateParameter("@address",  (object?)user.Address  ?? DBNull.Value),
                CreateParameter("@Role_ID",  (object?)user.Role_ID  ?? DBNull.Value)
            };

            var newIdScalar = await ExecuteScalarAsync<long>(sql, parameters, ct: ct);
            int newId = Convert.ToInt32(newIdScalar);
            user.User_ID = newId;
            return newId;
        }

        // ── Update all fields ─────────────────────────────────────────────────
        public async Task<bool> UpdateAsync(User user, CancellationToken ct = default)
        {
            if (user is null) throw new ArgumentNullException(nameof(user));

            const string sql = @"
                UPDATE users
                SET name     = @name,
                    email    = @email,
                    password = @password,
                    address  = @address,
                    Role_ID  = @Role_ID
                WHERE user_ID = @user_ID;";

            var parameters = new[]
            {
                CreateParameter("@user_ID",  user.User_ID),
                CreateParameter("@name",     user.Name),
                CreateParameter("@email",    (object?)user.Email   ?? DBNull.Value),
                CreateParameter("@password", user.Password),
                CreateParameter("@address",  (object?)user.Address ?? DBNull.Value),
                CreateParameter("@Role_ID",  (object?)user.Role_ID ?? DBNull.Value)
            };

            int rowsAffected = await ExecuteNonQueryAsync(sql, parameters, ct: ct);
            return rowsAffected > 0;
        }

        // ── Update password only ──────────────────────────────────────────────
        public async Task<bool> UpdatePasswordAsync(int userId, string newPasswordHash, CancellationToken ct = default)
        {
            const string sql = @"
                UPDATE users
                SET password = @password
                WHERE user_ID = @user_ID;";

            var parameters = new[]
            {
                CreateParameter("@password", newPasswordHash),
                CreateParameter("@user_ID",  userId)
            };

            int rowsAffected = await ExecuteNonQueryAsync(sql, parameters, ct: ct);
            return rowsAffected > 0;
        }

        // ── Delete ────────────────────────────────────────────────────────────
        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            const string sql = "DELETE FROM users WHERE user_ID = @user_ID;";
            var parameters   = new[] { CreateParameter("@user_ID", id) };
            int rowsAffected = await ExecuteNonQueryAsync(sql, parameters, ct: ct);
            return rowsAffected > 0;
        }

        // ── Mapper ────────────────────────────────────────────────────────────
        private static User MapUser(DbDataReader reader)
        {
            int roleIdOrdinal  = reader.GetOrdinal("Role_ID");
            int addressOrdinal = reader.GetOrdinal("address");

            return new User
            {
                User_ID  = ReadValue(reader, "user_ID",  0),
                Name     = ReadValue(reader, "name",     string.Empty),
                Email    = ReadValue(reader, "email",    string.Empty),
                Password = ReadValue(reader, "password", string.Empty),
                Address  = reader.IsDBNull(addressOrdinal) ? null : reader.GetString(addressOrdinal),
                Role_ID  = reader.IsDBNull(roleIdOrdinal)  ? null : Convert.ToInt32(reader.GetValue(roleIdOrdinal))
            };
        }
    }
}
