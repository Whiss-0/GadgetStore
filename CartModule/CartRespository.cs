using System.Data.Common;
using api.Main;

namespace api.CartModule
{
    public class CartRespository : BaseRepository, ICartRespository
    {
        public CartRespository(MyCon dbConnection) : base(dbConnection) { }

        private const string SelectCols = "cart_id, user_id, product_id, quantity";

        public async Task<Cart?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            var sql = $"SELECT {SelectCols} FROM cart WHERE cart_id = @id LIMIT 1;";
            var parameters = new[] { CreateParameter("@id", id) };
            var list = await ExecuteReaderToListAsync(sql, MapCart, parameters, ct: ct);
            return list.Count > 0 ? list[0] : null;
        }

        public async Task<List<Cart>> GetByUserAsync(int userId, CancellationToken ct = default)
        {
            var sql = $"SELECT {SelectCols} FROM cart WHERE user_id = @userId;";
            var parameters = new[] { CreateParameter("@userId", userId) };
            return await ExecuteReaderToListAsync(sql, MapCart, parameters, ct: ct);
        }

        public async Task<Cart?> GetByUserAndProductAsync(int userId, int productId, CancellationToken ct = default)
        {
            var sql = $"SELECT {SelectCols} FROM cart WHERE user_id = @userId AND product_id = @productId LIMIT 1;";
            var parameters = new[] { CreateParameter("@userId", userId), CreateParameter("@productId", productId) };
            var list = await ExecuteReaderToListAsync(sql, MapCart, parameters, ct: ct);
            return list.Count > 0 ? list[0] : null;
        }

        public async Task<int> CreateAsync(Cart cart, CancellationToken ct = default)
        {
            if (cart is null) throw new ArgumentNullException(nameof(cart));
            const string sql = @"
                INSERT INTO cart (user_id, product_id, quantity)
                VALUES (@user_id, @product_id, @quantity);
                SELECT last_insert_rowid();";
            var parameters = new[]
            {
                CreateParameter("@user_id", cart.user_id),
                CreateParameter("@product_id", cart.product_id),
                CreateParameter("@quantity", cart.quantity)
            };
            var newIdScalar = await ExecuteScalarAsync<long>(sql, parameters, ct: ct);
            int newId = Convert.ToInt32(newIdScalar);
            cart.cart_id = newId;
            return newId;
        }

        public async Task<bool> UpdateQuantityAsync(int cartId, int quantity, CancellationToken ct = default)
        {
            const string sql = "UPDATE cart SET quantity = @quantity WHERE cart_id = @id;";
            var parameters = new[] { CreateParameter("@quantity", quantity), CreateParameter("@id", cartId) };
            int rows = await ExecuteNonQueryAsync(sql, parameters, ct: ct);
            return rows > 0;
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            const string sql = "DELETE FROM cart WHERE cart_id = @id;";
            var parameters = new[] { CreateParameter("@id", id) };
            int rows = await ExecuteNonQueryAsync(sql, parameters, ct: ct);
            return rows > 0;
        }

        public async Task<bool> ClearCartAsync(int userId, CancellationToken ct = default)
        {
            const string sql = "DELETE FROM cart WHERE user_id = @userId;";
            var parameters = new[] { CreateParameter("@userId", userId) };
            int rows = await ExecuteNonQueryAsync(sql, parameters, ct: ct);
            return rows >= 0;
        }

        private static Cart MapCart(DbDataReader reader) => new Cart
        {
            cart_id = ReadValue(reader, "cart_id", 0),
            user_id = ReadValue(reader, "user_id", 0),
            product_id = ReadValue(reader, "product_id", 0),
            quantity = ReadValue(reader, "quantity", 0)
        };
    }
}
