using System.Data.Common;
using api.Main;

namespace api.OrderModule
{
    public class OrderRepository : BaseRepository, IOrderRepository
    {
        public OrderRepository(MyCon dbConnection) : base(dbConnection) { }

        private const string SelectCols = "order_id, user_id, order_date, total_amount, subtotal_amount, delivery_fee_amount, vat_amount, vat_rate_percent, vat_price_mode, status, shipping_address, shipping_city_code, shipping_province_code, phone_number, payment_method, payment_status, courier_name, tracking_number, estimated_delivery_date";

        public async Task<Order?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            var sql = $"SELECT {SelectCols} FROM orders WHERE order_id = @id LIMIT 1;";
            var parameters = new[] { CreateParameter("@id", id) };
            var list = await ExecuteReaderToListAsync(sql, MapOrder, parameters, ct: ct);
            return list.Count > 0 ? list[0] : null;
        }

        public Task<List<OrderDeliveryEvent>> GetDeliveryEventsAsync(int orderId, CancellationToken ct = default)
        {
            const string sql = @"
                SELECT delivery_event_id, status, event_type, note, created_at, actor_role
                FROM order_delivery_events
                WHERE order_id = @order_id
                ORDER BY created_at ASC, delivery_event_id ASC;";
            return ExecuteReaderToListAsync(sql, MapDeliveryEvent,
                new[] { CreateParameter("@order_id", orderId) }, ct: ct);
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
                INSERT INTO orders (user_id, order_date, total_amount, subtotal_amount, delivery_fee_amount, vat_amount, vat_rate_percent, vat_price_mode, status, shipping_address, shipping_city_code, shipping_province_code, phone_number, payment_method, payment_status)
                VALUES (@user_id, @order_date, @total_amount, @subtotal_amount, @delivery_fee_amount, @vat_amount, @vat_rate_percent, @vat_price_mode, @status, @shipping_address, @shipping_city_code, @shipping_province_code, @phone_number, @payment_method, @payment_status);
                SELECT last_insert_rowid();";
            var parameters = new[]
            {
                CreateParameter("@user_id", order.user_id),
                CreateParameter("@order_date", order.order_date.ToString("yyyy-MM-dd HH:mm:ss")),
                CreateParameter("@total_amount", order.total_amount),
                CreateParameter("@status", order.status),
                CreateParameter("@shipping_address", (object?)order.shipping_address ?? DBNull.Value),
                CreateParameter("@shipping_city_code", (object?)order.shipping_city_code ?? DBNull.Value),
                CreateParameter("@shipping_province_code", (object?)order.shipping_province_code ?? DBNull.Value),
                CreateParameter("@phone_number", (object?)order.phone_number ?? DBNull.Value),
                CreateParameter("@payment_method", order.payment_method),
                CreateParameter("@payment_status", order.payment_status),
                CreateParameter("@subtotal_amount", order.subtotal_amount),
                CreateParameter("@delivery_fee_amount", order.delivery_fee_amount),
                CreateParameter("@vat_amount", order.vat_amount),
                CreateParameter("@vat_rate_percent", order.vat_rate_percent),
                CreateParameter("@vat_price_mode", order.vat_price_mode)
            };
            int newId = await WithTransactionAsync(async (conn, tx) =>
            {
                var newIdScalar = await ExecuteScalarAsync<long>(conn, tx, sql, parameters, ct: ct);
                int id = Convert.ToInt32(newIdScalar);
                await InsertDeliveryEventAsync(conn, tx, id, order.status, "OrderCreated", "Order placed.", "User", order.order_date, ct);
                return id;
            }, ct: ct);
            order.order_id = newId;
            return newId;
        }

        public async Task<bool> UpdateAsync(Order order, CancellationToken ct = default)
        {
            if (order is null) throw new ArgumentNullException(nameof(order));
            const string sql = "UPDATE orders SET user_id = @user_id, order_date = @order_date, total_amount = @total_amount, subtotal_amount = @subtotal_amount, delivery_fee_amount = @delivery_fee_amount, vat_amount = @vat_amount, vat_rate_percent = @vat_rate_percent, vat_price_mode = @vat_price_mode, status = @status, shipping_address = @shipping_address, shipping_city_code = @shipping_city_code, shipping_province_code = @shipping_province_code, phone_number = @phone_number, payment_method = @payment_method, payment_status = @payment_status, courier_name = @courier_name, tracking_number = @tracking_number, estimated_delivery_date = @estimated_delivery_date WHERE order_id = @id;";
            var parameters = new[]
            {
                CreateParameter("@user_id", order.user_id),
                CreateParameter("@order_date", order.order_date.ToString("yyyy-MM-dd HH:mm:ss")),
                CreateParameter("@total_amount", order.total_amount),
                CreateParameter("@status", order.status),
                CreateParameter("@shipping_address", (object?)order.shipping_address ?? DBNull.Value),
                CreateParameter("@shipping_city_code", (object?)order.shipping_city_code ?? DBNull.Value),
                CreateParameter("@shipping_province_code", (object?)order.shipping_province_code ?? DBNull.Value),
                CreateParameter("@phone_number", (object?)order.phone_number ?? DBNull.Value),
                CreateParameter("@payment_method", order.payment_method),
                CreateParameter("@payment_status", order.payment_status),
                CreateParameter("@subtotal_amount", order.subtotal_amount),
                CreateParameter("@delivery_fee_amount", order.delivery_fee_amount),
                CreateParameter("@vat_amount", order.vat_amount),
                CreateParameter("@vat_rate_percent", order.vat_rate_percent),
                CreateParameter("@vat_price_mode", order.vat_price_mode),
                CreateParameter("@courier_name", (object?)order.courier_name ?? DBNull.Value),
                CreateParameter("@tracking_number", (object?)order.tracking_number ?? DBNull.Value),
                CreateParameter("@estimated_delivery_date", (object?)order.estimated_delivery_date ?? DBNull.Value),
                CreateParameter("@id", order.order_id)
            };
            int rows = await ExecuteNonQueryAsync(sql, parameters, ct: ct);
            return rows > 0;
        }

        public Task<bool> UpdateStatusWithEventAsync(int orderId, string expectedStatus, string newStatus, string actorRole, string? note, CancellationToken ct = default)
        {
            return WithTransactionAsync(async (conn, tx) =>
            {
                const string sql = "UPDATE orders SET status = @status WHERE order_id = @id AND status = @expected_status;";
                int changed = await ExecuteNonQueryAsync(conn, tx, sql, new[]
                {
                    CreateParameter("@status", newStatus),
                    CreateParameter("@id", orderId),
                    CreateParameter("@expected_status", expectedStatus),
                }, ct: ct);
                if (changed == 0) return false;

                await InsertDeliveryEventAsync(conn, tx, orderId, newStatus,
                    newStatus == "Cancelled" ? "Cancelled" : "StatusChanged", note, actorRole, DateTime.UtcNow, ct);
                return true;
            }, ct: ct);
        }

        public Task<bool> UpdateFulfillmentWithEventAsync(Order order, string actorRole, string? note, CancellationToken ct = default)
        {
            return WithTransactionAsync(async (conn, tx) =>
            {
                const string sql = @"
                    UPDATE orders
                    SET courier_name = @courier_name,
                        tracking_number = @tracking_number,
                        estimated_delivery_date = @estimated_delivery_date
                    WHERE order_id = @id;";
                int changed = await ExecuteNonQueryAsync(conn, tx, sql, new[]
                {
                    CreateParameter("@courier_name", (object?)order.courier_name ?? DBNull.Value),
                    CreateParameter("@tracking_number", (object?)order.tracking_number ?? DBNull.Value),
                    CreateParameter("@estimated_delivery_date", (object?)order.estimated_delivery_date ?? DBNull.Value),
                    CreateParameter("@id", order.order_id),
                }, ct: ct);
                if (changed == 0) return false;

                await InsertDeliveryEventAsync(conn, tx, order.order_id, order.status,
                    "TrackingUpdated", note, actorRole, DateTime.UtcNow, ct);
                return true;
            }, ct: ct);
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            const string sql = "DELETE FROM orders WHERE order_id = @id;";
            var parameters = new[] { CreateParameter("@id", id) };
            int rows = await ExecuteNonQueryAsync(sql, parameters, ct: ct);
            return rows > 0;
        }

        public async Task<int> CreateWithDetailsAsync(
            Order order,
            IReadOnlyList<api.OrderDetailModule.OrderDetail> details,
            CancellationToken ct = default)
        {
            if (order is null) throw new ArgumentNullException(nameof(order));
            if (details is null || details.Count == 0)
                throw new ArgumentException("At least one order item is required.", nameof(details));

            // Merge duplicate product IDs before entering the transaction to avoid
            // double stock decrements on the same product.
            var mergedDetails = details
                .GroupBy(d => d.product_id)
                .Select(g => new api.OrderDetailModule.OrderDetail
                {
                    product_id = g.Key,
                    quantity   = g.Sum(d => d.quantity),
                    price      = 0m, // will be overwritten by DB price below
                })
                .ToList();

            return await WithTransactionAsync(async (conn, tx) =>
            {
                // Load settings inside the order transaction so the calculation and its
                // order-time snapshot use one consistent settings row.
                const string settingsSql = @"
                    SELECT delivery_fee_amount, vat_enabled, vat_rate_percent, vat_price_mode, vat_delivery_taxable
                    FROM store_pricing_settings
                    WHERE id = 1
                    LIMIT 1;";
                var settingsRows = await ExecuteReaderToListAsync(conn, tx, settingsSql, reader => (
                    deliveryFee: ReadValue(reader, "delivery_fee_amount", 0m),
                    vatEnabled: ReadValue(reader, "vat_enabled", 0) != 0,
                    vatRatePercent: ReadValue(reader, "vat_rate_percent", 12m),
                    vatPriceMode: ReadValue(reader, "vat_price_mode", "unconfirmed"),
                    vatDeliveryTaxable: ReadValue(reader, "vat_delivery_taxable", 0) != 0
                ), ct: ct);
                if (settingsRows.Count == 0)
                    throw new InvalidOperationException("Store pricing settings are not initialized. Apply the one-time database migration.");

                var pricing = settingsRows[0];
                if (pricing.deliveryFee < 0 || pricing.vatRatePercent < 0 || pricing.vatRatePercent > 100)
                    throw new InvalidOperationException("Store pricing settings contain an invalid fee or VAT rate.");
                if (pricing.vatEnabled && pricing.vatPriceMode != "inclusive" && pricing.vatPriceMode != "exclusive")
                    throw new InvalidOperationException("VAT cannot be enabled until the price convention is confirmed as inclusive or exclusive.");

                // Validate the requested area and resolve its fee inside this same transaction.
                // Codes are TEXT/PSGC values; they are never converted to numbers.
                const string areaSql = @"
                    SELECT p.province_code, c.city_code
                    FROM ph_provinces p
                    JOIN ph_cities c ON c.province_code = p.province_code
                    WHERE p.province_code = @province_code AND c.city_code = @city_code
                    LIMIT 1;";
                var areaRows = await ExecuteReaderToListAsync(conn, tx, areaSql,
                    reader => (province: ReadValue(reader, "province_code", string.Empty),
                               city: ReadValue(reader, "city_code", string.Empty)),
                    new[]
                    {
                        CreateParameter("@province_code", order.shipping_province_code ?? string.Empty),
                        CreateParameter("@city_code", order.shipping_city_code ?? string.Empty),
                    }, ct: ct);
                if (areaRows.Count == 0)
                    throw new InvalidOperationException("The supplied provinceCode and cityCode are invalid or the city/municipality does not belong to that province.");

                const string feeSql = @"
                    SELECT CASE
                        WHEN EXISTS (SELECT 1 FROM delivery_fee_rules WHERE province_code = @province_code AND city_code = @city_code)
                        THEN (SELECT delivery_fee_amount FROM delivery_fee_rules WHERE province_code = @province_code AND city_code = @city_code LIMIT 1)
                        WHEN EXISTS (SELECT 1 FROM delivery_fee_rules WHERE province_code = @province_code AND city_code IS NULL)
                        THEN (SELECT delivery_fee_amount FROM delivery_fee_rules WHERE province_code = @province_code AND city_code IS NULL LIMIT 1)
                        ELSE @fallback_fee
                    END AS delivery_fee_amount;";
                var areaFees = await ExecuteReaderToListAsync(conn, tx, feeSql,
                    reader => ReadValue(reader, "delivery_fee_amount", 0m),
                    new[]
                    {
                        CreateParameter("@province_code", order.shipping_province_code!),
                        CreateParameter("@city_code", order.shipping_city_code!),
                        CreateParameter("@fallback_fee", pricing.deliveryFee),
                    }, ct: ct);
                if (areaFees.Count == 0)
                    throw new InvalidOperationException("Unable to resolve a delivery fee for the selected area.");
                decimal resolvedDeliveryFee = areaFees[0];
                if (resolvedDeliveryFee < 0)
                    throw new InvalidOperationException("The resolved delivery fee is invalid.");

                // 1. Validate every item and calculate the authoritative DB-priced subtotal.
                decimal subtotal = 0m;
                var pricedDetails = new List<(int productId, int quantity, decimal dbPrice)>();

                foreach (var detail in mergedDetails)
                {
                    if (detail.quantity < 1)
                        throw new InvalidOperationException($"Quantity for product {detail.product_id} must be at least 1.");

                    // Read current price and stock from the database — never trust the client.
                    const string productSql = "SELECT price, stock FROM products WHERE product_id = @pid LIMIT 1;";
                    var productParams = new[] { CreateParameter("@pid", detail.product_id) };

                    var productRows = await ExecuteReaderToListAsync(
                        conn, tx, productSql,
                        reader => (
                            dbPrice: ReadValue(reader, "price", 0m),
                            stock:   ReadValue(reader, "stock", -1)
                        ),
                        productParams, ct: ct);

                    if (productRows.Count == 0)
                        throw new InvalidOperationException($"Product {detail.product_id} does not exist.");

                    var (dbPrice, stock) = productRows[0];

                    if (stock < detail.quantity)
                        throw new InvalidOperationException($"Product {detail.product_id} is out of stock or has insufficient stock.");

                    subtotal = checked(subtotal + dbPrice * detail.quantity);
                    pricedDetails.Add((detail.product_id, detail.quantity, dbPrice));
                }

                subtotal = RoundMoney(subtotal);
                decimal deliveryFee = RoundMoney(resolvedDeliveryFee);
                decimal vatAmount = 0m;
                string orderVatPriceMode = pricing.vatEnabled ? pricing.vatPriceMode : "disabled";
                decimal taxableBase = subtotal + (pricing.vatDeliveryTaxable ? deliveryFee : 0m);
                if (pricing.vatEnabled)
                {
                    vatAmount = pricing.vatPriceMode == "exclusive"
                        ? RoundMoney(taxableBase * pricing.vatRatePercent / 100m)
                        : RoundMoney(taxableBase * pricing.vatRatePercent / (100m + pricing.vatRatePercent));
                }
                decimal authoritativeTotal = pricing.vatEnabled && pricing.vatPriceMode == "exclusive"
                    ? RoundMoney(subtotal + deliveryFee + vatAmount)
                    : RoundMoney(subtotal + deliveryFee);

                // 2. Insert the order using the authoritative total calculated above.

                const string insertOrder = @"
                    INSERT INTO orders (user_id, order_date, total_amount, subtotal_amount, delivery_fee_amount, vat_amount, vat_rate_percent, vat_price_mode, status, shipping_address, shipping_city_code, shipping_province_code, phone_number, payment_method, payment_status)
                    VALUES (@user_id, @order_date, @total_amount, @subtotal_amount, @delivery_fee_amount, @vat_amount, @vat_rate_percent, @vat_price_mode, @status, @shipping_address, @shipping_city_code, @shipping_province_code, @phone_number, @payment_method, @payment_status);
                    SELECT last_insert_rowid();";

                var orderParams = new[]
                {
                    CreateParameter("@user_id", order.user_id),
                    CreateParameter("@order_date", order.order_date.ToString("yyyy-MM-dd HH:mm:ss")),
                    CreateParameter("@total_amount", authoritativeTotal),   // DB-calculated, not client value
                    CreateParameter("@subtotal_amount", subtotal),
                    CreateParameter("@delivery_fee_amount", deliveryFee),
                    CreateParameter("@vat_amount", vatAmount),
                    CreateParameter("@vat_rate_percent", pricing.vatRatePercent),
                    CreateParameter("@vat_price_mode", orderVatPriceMode),
                    CreateParameter("@status", order.status),
                    CreateParameter("@shipping_address", (object?)order.shipping_address ?? DBNull.Value),
                    CreateParameter("@shipping_city_code", (object?)order.shipping_city_code ?? DBNull.Value),
                    CreateParameter("@shipping_province_code", (object?)order.shipping_province_code ?? DBNull.Value),
                    CreateParameter("@phone_number", (object?)order.phone_number ?? DBNull.Value),
                    CreateParameter("@payment_method", order.payment_method),
                    CreateParameter("@payment_status", order.payment_status),
                };

                var newIdScalar = await ExecuteScalarAsync<long>(conn, tx, insertOrder, orderParams, ct: ct);
                int newOrderId = Convert.ToInt32(newIdScalar);
                order.order_id      = newOrderId;
                order.total_amount  = authoritativeTotal;
                order.subtotal_amount = subtotal;
                order.delivery_fee_amount = deliveryFee;
                order.vat_amount = vatAmount;
                order.vat_rate_percent = pricing.vatRatePercent;
                order.vat_price_mode = orderVatPriceMode;
                await InsertDeliveryEventAsync(conn, tx, newOrderId, order.status, "OrderCreated", "Order placed.", "User", order.order_date, ct);

                // 3. Decrement stock and insert detail lines using DB prices.
                foreach (var (productId, quantity, dbPrice) in pricedDetails)
                {
                    const string decrementSql = @"
                        UPDATE products SET stock = stock - @qty
                        WHERE product_id = @pid AND stock >= @qty;";
                    var decrParams = new[]
                    {
                        CreateParameter("@qty", quantity),
                        CreateParameter("@pid", productId),
                    };
                    int affected = await ExecuteNonQueryAsync(conn, tx, decrementSql, decrParams, ct: ct);
                    if (affected == 0)
                        throw new InvalidOperationException($"Product {productId} is out of stock or has insufficient stock.");

                    const string insertDetail = @"
                        INSERT INTO order_details (order_id, product_id, quantity, price)
                        VALUES (@order_id, @product_id, @quantity, @price);";
                    var detailParams = new[]
                    {
                        CreateParameter("@order_id",   newOrderId),
                        CreateParameter("@product_id", productId),
                        CreateParameter("@quantity",   quantity),
                        CreateParameter("@price",      dbPrice),   // DB price, not client price
                    };
                    await ExecuteNonQueryAsync(conn, tx, insertDetail, detailParams, ct: ct);
                }

                return newOrderId;
            }, ct: ct);
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
                order_id         = ReadValue(reader, "order_id", 0),
                user_id          = ReadValue(reader, "user_id", 0),
                order_date       = orderDate,
                total_amount     = totalAmount,
                subtotal_amount = ReadValue(reader, "subtotal_amount", 0m),
                delivery_fee_amount = ReadValue(reader, "delivery_fee_amount", 0m),
                vat_amount = ReadValue(reader, "vat_amount", 0m),
                vat_rate_percent = ReadValue(reader, "vat_rate_percent", 0m),
                vat_price_mode = ReadValue(reader, "vat_price_mode", "disabled"),
                status           = ReadValue(reader, "status", "Pending"),
                shipping_address = ReadValue<string?>(reader, "shipping_address", null),
                shipping_city_code = ReadValue<string?>(reader, "shipping_city_code", null),
                shipping_province_code = ReadValue<string?>(reader, "shipping_province_code", null),
                phone_number     = ReadValue<string?>(reader, "phone_number", null),
                payment_method   = ReadValue(reader, "payment_method", "COD"),
                payment_status   = ReadValue(reader, "payment_status", "Unpaid"),
                courier_name     = ReadValue<string?>(reader, "courier_name", null),
                tracking_number  = ReadValue<string?>(reader, "tracking_number", null),
                estimated_delivery_date = ReadValue<string?>(reader, "estimated_delivery_date", null)
            };
        }

        private static OrderDeliveryEvent MapDeliveryEvent(DbDataReader reader)
        {
            string createdAt = ReadValue(reader, "created_at", DateTime.UtcNow.ToString("o"));
            return new OrderDeliveryEvent
            {
                delivery_event_id = ReadValue(reader, "delivery_event_id", 0L),
                status = ReadValue(reader, "status", "Pending"),
                event_type = ReadValue(reader, "event_type", string.Empty),
                note = ReadValue<string?>(reader, "note", null),
                created_at = DateTime.TryParse(createdAt, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var parsed) ? parsed : DateTime.UtcNow,
                actor_role = ReadValue(reader, "actor_role", "System"),
            };
        }

        private static async Task InsertDeliveryEventAsync(DbConnection conn, DbTransaction tx, int orderId,
            string status, string eventType, string? note, string actorRole, DateTime createdAt, CancellationToken ct)
        {
            const string sql = @"
                INSERT INTO order_delivery_events (order_id, status, event_type, note, created_at, actor_role)
                VALUES (@order_id, @status, @event_type, @note, @created_at, @actor_role);";
            await ExecuteNonQueryAsync(conn, tx, sql, new[]
            {
                CreateParameter("@order_id", orderId),
                CreateParameter("@status", status),
                CreateParameter("@event_type", eventType),
                CreateParameter("@note", (object?)note ?? DBNull.Value),
                CreateParameter("@created_at", createdAt.ToUniversalTime().ToString("o")),
                CreateParameter("@actor_role", actorRole),
            }, ct: ct);
        }

        private static decimal RoundMoney(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
    }
}
