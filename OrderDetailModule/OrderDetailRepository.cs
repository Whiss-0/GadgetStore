using System.Data.Common;
using api.Main;

namespace api.OrderDetailModule
{
    public class OrderDetailRepository : BaseRepository, IOrderDetailRepository
    {
        public OrderDetailRepository(MyCon dbConnection) : base(dbConnection) { }

        private const string SelectCols = "order_detail_id, order_id, product_id, quantity, price";

        public async Task<OrderDetail?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            var sql = $"SELECT {SelectCols} FROM order_details WHERE order_detail_id = @id LIMIT 1;";
            var parameters = new[] { CreateParameter("@id", id) };
            var list = await ExecuteReaderToListAsync(sql, MapDetail, parameters, ct: ct);
            return list.Count > 0 ? list[0] : null;
        }

        public async Task<List<OrderDetail>> GetByOrderAsync(int orderId, CancellationToken ct = default)
        {
            var sql = $"SELECT {SelectCols} FROM order_details WHERE order_id = @orderId;";
            var parameters = new[] { CreateParameter("@orderId", orderId) };
            return await ExecuteReaderToListAsync(sql, MapDetail, parameters, ct: ct);
        }

        public async Task<List<OrderDetail>> GetAllAsync(CancellationToken ct = default)
        {
            var sql = $"SELECT {SelectCols} FROM order_details;";
            return await ExecuteReaderToListAsync(sql, MapDetail, ct: ct);
        }

        public async Task<int> CreateAsync(OrderDetail detail, CancellationToken ct = default)
        {
            if (detail is null) throw new ArgumentNullException(nameof(detail));
            const string sql = @"
                INSERT INTO order_details (order_id, product_id, quantity, price)
                VALUES (@order_id, @product_id, @quantity, @price);
                SELECT last_insert_rowid();";
            var parameters = new[]
            {
                CreateParameter("@order_id", detail.order_id),
                CreateParameter("@product_id", detail.product_id),
                CreateParameter("@quantity", detail.quantity),
                CreateParameter("@price", detail.price)
            };
            var newIdScalar = await ExecuteScalarAsync<long>(sql, parameters, ct: ct);
            int newId = Convert.ToInt32(newIdScalar);
            detail.order_detail_id = newId;
            return newId;
        }

        public async Task<bool> UpdateAsync(OrderDetail detail, CancellationToken ct = default)
        {
            if (detail is null) throw new ArgumentNullException(nameof(detail));
            const string sql = "UPDATE order_details SET order_id = @order_id, product_id = @product_id, quantity = @quantity, price = @price WHERE order_detail_id = @id;";
            var parameters = new[]
            {
                CreateParameter("@order_id", detail.order_id),
                CreateParameter("@product_id", detail.product_id),
                CreateParameter("@quantity", detail.quantity),
                CreateParameter("@price", detail.price),
                CreateParameter("@id", detail.order_detail_id)
            };
            int rows = await ExecuteNonQueryAsync(sql, parameters, ct: ct);
            return rows > 0;
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            const string sql = "DELETE FROM order_details WHERE order_detail_id = @id;";
            var parameters = new[] { CreateParameter("@id", id) };
            int rows = await ExecuteNonQueryAsync(sql, parameters, ct: ct);
            return rows > 0;
        }

        private static OrderDetail MapDetail(DbDataReader reader) => new OrderDetail
        {
            order_detail_id = ReadValue(reader, "order_detail_id", 0),
            order_id = ReadValue(reader, "order_id", 0),
            product_id = ReadValue(reader, "product_id", 0),
            quantity = ReadValue(reader, "quantity", 0),
            price = ReadValue(reader, "price", 0m)
        };
    }
}
