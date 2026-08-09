using System.Data.Common;
using api.Main;

namespace api.OrderModule
{
    public class OrderRepository : BaseRepository, IOrderRepository
    {
        public OrderRepository(MyCon dbConnection) : base(dbConnection) { }

        private const string SelectCols = "order_id, user_id, order_date, total_amount, status";

        public async Task<Order?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            var sql = $"SELECT {SelectCols} FROM orders WHERE order_id = @id LIMIT 1;";
            var parameters = new[] { CreateParameter("@id", id) };
            var list = await ExecuteReaderToListAsync(sql, MapOrder, parameters, ct: ct);
            return list.Count > 0 ? list[0] : null;
        }

        public async Task<List<Order>> GetAllAsync(CancellationToken ct = default)
        {
            var sql = $"SELECT {SelectCols} FROM orders ORDER BY order_date DESC;";
            return await ExecuteReaderToListAsync(sql, MapOrder, ct: ct);
        }

        public async Task<List<Order>> GetByUserAsync(int userId, CancellationToken ct = default)
        {
            var sql = $"SELECT {SelectCols} FROM orders WHERE user_id = @userId ORDER BY order_date DESC;";
            var parameters = new[] { CreateParameter("@userId", userId) };
            return await ExecuteReaderToListAsync(sql, MapOrder, parameters, ct: ct);
        }

        public async Task<PaginationModel<Order>> GetPagedAsync(int pageNumber, int pageSize, CancellationToken ct = default)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 10;
            int offset = (pageNumber - 1) * pageSize;

            var totalCountScalar = await ExecuteScalarAsync<long>("SELECT COUNT(*) FROM orders;", ct: ct);
            int totalCount = Convert.ToInt32(totalCountScalar);

            var sql = $"SELECT {SelectCols} FROM orders ORDER BY order_date DESC LIMIT @pageSize OFFSET @offset;";
            var parameters = new[] { CreateParameter("@pageSize", pageSize), CreateParameter("@offset", offset) };
            var items = await ExecuteReaderToListAsync(sql, MapOrder, parameters, ct: ct);

            return new PaginationModel<Order> { Items = items, TotalCount = totalCount, PageSize = pageSize, CurrentPage = pageNumber };
        }

        public async Task<int> CreateAsync(Order order, CancellationToken ct = default)
        {
            if (order is null) throw new ArgumentNullException(nameof(order));
            const string sql = @"
                INSERT INTO orders (user_id, order_date, total_amount, status)
                VALUES (@user_id, @order_date, @total_amount, @status);
                SELECT last_insert_rowid();";
            var parameters = new[]
            {
                CreateParameter("@user_id", order.user_id),
                CreateParameter("@order_date", order.order_date.ToString("yyyy-MM-dd HH:mm:ss")),
                CreateParameter("@total_amount", order.total_amount),
                CreateParameter("@status", order.status)
            };
            var newIdScalar = await ExecuteScalarAsync<long>(sql, parameters, ct: ct);
            int newId = Convert.ToInt32(newIdScalar);
            order.order_id = newId;
            return newId;
        }

        public async Task<bool> UpdateAsync(Order order, CancellationToken ct = default)
        {
            if (order is null) throw new ArgumentNullException(nameof(order));
            const string sql = "UPDATE orders SET user_id = @user_id, order_date = @order_date, total_amount = @total_amount, status = @status WHERE order_id = @id;";
            var parameters = new[]
            {
                CreateParameter("@user_id", order.user_id),
                CreateParameter("@order_date", order.order_date.ToString("yyyy-MM-dd HH:mm:ss")),
                CreateParameter("@total_amount", order.total_amount),
                CreateParameter("@status", order.status),
                CreateParameter("@id", order.order_id)
            };
            int rows = await ExecuteNonQueryAsync(sql, parameters, ct: ct);
            return rows > 0;
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            const string sql = "DELETE FROM orders WHERE order_id = @id;";
            var parameters = new[] { CreateParameter("@id", id) };
            int rows = await ExecuteNonQueryAsync(sql, parameters, ct: ct);
            return rows > 0;
        }

        private static Order MapOrder(DbDataReader reader)
        {
            // Defensive date parsing — SQLite stores dates as TEXT; handle malformed values.
            DateTime orderDate;
            try { orderDate = Convert.ToDateTime(reader.GetValue(reader.GetOrdinal("order_date"))); }
            catch { orderDate = DateTime.UtcNow; }

            // Defensive decimal parsing — if someone stored '' or a bad value, default to 0.
            decimal totalAmount;
            try { totalAmount = ReadValue(reader, "total_amount", 0m); }
            catch { totalAmount = 0m; }

            return new Order
            {
                order_id    = ReadValue(reader, "order_id", 0),
                user_id     = ReadValue(reader, "user_id", 0),
                order_date  = orderDate,
                total_amount = totalAmount,
                status      = ReadValue(reader, "status", "Pending")
            };
        }
    }
}
