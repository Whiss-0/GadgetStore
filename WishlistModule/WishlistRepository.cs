using System.Data.Common;
using api.Main;

namespace api.WishlistModule
{
    public class WishlistRepository : BaseRepository, IWishlistRepository
    {
        public WishlistRepository(MyCon dbConnection) : base(dbConnection) { }

        private const string SelectCols = "wishlist_id, user_id, product_id";

        public async Task<Wishlist?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            var sql = $"SELECT {SelectCols} FROM wishlist WHERE wishlist_id = @id LIMIT 1;";
            var parameters = new[] { CreateParameter("@id", id) };
            var list = await ExecuteReaderToListAsync(sql, MapWishlist, parameters, ct: ct);
            return list.Count > 0 ? list[0] : null;
        }

        public async Task<List<Wishlist>> GetByUserAsync(int userId, CancellationToken ct = default)
        {
            var sql = $"SELECT {SelectCols} FROM wishlist WHERE user_id = @userId;";
            var parameters = new[] { CreateParameter("@userId", userId) };
            return await ExecuteReaderToListAsync(sql, MapWishlist, parameters, ct: ct);
        }

        public async Task<Wishlist?> GetByUserAndProductAsync(int userId, int productId, CancellationToken ct = default)
        {
            var sql = $"SELECT {SelectCols} FROM wishlist WHERE user_id = @userId AND product_id = @productId LIMIT 1;";
            var parameters = new[] { CreateParameter("@userId", userId), CreateParameter("@productId", productId) };
            var list = await ExecuteReaderToListAsync(sql, MapWishlist, parameters, ct: ct);
            return list.Count > 0 ? list[0] : null;
        }

        public async Task<int> CreateAsync(Wishlist wishlist, CancellationToken ct = default)
        {
            if (wishlist is null) throw new ArgumentNullException(nameof(wishlist));
            const string sql = @"
                INSERT INTO wishlist (user_id, product_id) VALUES (@user_id, @product_id);
                SELECT last_insert_rowid();";
            var parameters = new[]
            {
                CreateParameter("@user_id", wishlist.user_id),
                CreateParameter("@product_id", wishlist.product_id)
            };
            var newIdScalar = await ExecuteScalarAsync<long>(sql, parameters, ct: ct);
            int newId = Convert.ToInt32(newIdScalar);
            wishlist.wishlist_id = newId;
            return newId;
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            const string sql = "DELETE FROM wishlist WHERE wishlist_id = @id;";
            var parameters = new[] { CreateParameter("@id", id) };
            int rows = await ExecuteNonQueryAsync(sql, parameters, ct: ct);
            return rows > 0;
        }

        public async Task<bool> ClearWishlistAsync(int userId, CancellationToken ct = default)
        {
            const string sql = "DELETE FROM wishlist WHERE user_id = @userId;";
            var parameters = new[] { CreateParameter("@userId", userId) };
            int rows = await ExecuteNonQueryAsync(sql, parameters, ct: ct);
            return rows >= 0;
        }

        private static Wishlist MapWishlist(DbDataReader reader) => new Wishlist
        {
            wishlist_id = ReadValue(reader, "wishlist_id", 0),
            user_id = ReadValue(reader, "user_id", 0),
            product_id = ReadValue(reader, "product_id", 0)
        };
    }
}
