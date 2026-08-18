using System.Data.Common;
using api.Main;

namespace api.ProductsModule
{
    public class ProductRepository : BaseRepository, IProductRepository
    {
        public ProductRepository(MyCon dbConnection) : base(dbConnection) { }

        private const string SelectCols = "product_id, category_id, product_name, brand, price, description, image, stock, ram_gb, processor, storage_gb";

        public async Task<Product?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            var sql = $"SELECT {SelectCols} FROM products WHERE product_id = @id LIMIT 1;";
            var parameters = new[] { CreateParameter("@id", id) };
            var list = await ExecuteReaderToListAsync(sql, MapProduct, parameters, ct: ct);
            return list.Count > 0 ? list[0] : null;
        }

        public async Task<List<Product>> GetAllAsync(CancellationToken ct = default)
        {
            var sql = $"SELECT {SelectCols} FROM products;";
            return await ExecuteReaderToListAsync(sql, MapProduct, ct: ct);
        }

        public async Task<PaginationModel<Product>> GetPagedAsync(int pageNumber, int pageSize, CancellationToken ct = default)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 10;
            int offset = (pageNumber - 1) * pageSize;

            var totalCountScalar = await ExecuteScalarAsync<long>("SELECT COUNT(*) FROM products;", ct: ct);
            int totalCount = Convert.ToInt32(totalCountScalar);

            var sql = $"SELECT {SelectCols} FROM products LIMIT @pageSize OFFSET @offset;";
            var parameters = new[] { CreateParameter("@pageSize", pageSize), CreateParameter("@offset", offset) };
            var items = await ExecuteReaderToListAsync(sql, MapProduct, parameters, ct: ct);

            return new PaginationModel<Product> { Items = items, TotalCount = totalCount, PageSize = pageSize, CurrentPage = pageNumber };
        }

        public async Task<List<Product>> GetByCategoryAsync(int categoryId, CancellationToken ct = default)
        {
            var sql = $"SELECT {SelectCols} FROM products WHERE category_id = @categoryId;";
            var parameters = new[] { CreateParameter("@categoryId", categoryId) };
            return await ExecuteReaderToListAsync(sql, MapProduct, parameters, ct: ct);
        }

        public async Task<List<Product>> SearchAsync(string term, CancellationToken ct = default)
        {
            var sql = $"SELECT {SelectCols} FROM products WHERE product_name LIKE @term OR brand LIKE @term;";
            var parameters = new[] { CreateParameter("@term", $"%{term}%") };
            return await ExecuteReaderToListAsync(sql, MapProduct, parameters, ct: ct);
        }

        public async Task<int> CreateAsync(Product product, CancellationToken ct = default)
        {
            if (product is null) throw new ArgumentNullException(nameof(product));
            const string sql = @"
                INSERT INTO products (category_id, product_name, brand, price, description, image, stock, ram_gb, processor, storage_gb)
                VALUES (@category_id, @product_name, @brand, @price, @description, @image, @stock, @ram_gb, @processor, @storage_gb);
                SELECT last_insert_rowid();";
            var parameters = new[]
            {
                CreateParameter("@category_id", (object?)product.category_id ?? DBNull.Value),
                CreateParameter("@product_name", product.product_name),
                CreateParameter("@brand", (object?)product.brand ?? DBNull.Value),
                CreateParameter("@price", product.price),
                CreateParameter("@description", (object?)product.description ?? DBNull.Value),
                CreateParameter("@image", (object?)product.image ?? DBNull.Value),
                CreateParameter("@stock", product.stock),
                CreateParameter("@ram_gb", (object?)product.ram_gb ?? DBNull.Value),
                CreateParameter("@processor", (object?)product.processor ?? DBNull.Value),
                CreateParameter("@storage_gb", (object?)product.storage_gb ?? DBNull.Value)
            };
            var newIdScalar = await ExecuteScalarAsync<long>(sql, parameters, ct: ct);
            int newId = Convert.ToInt32(newIdScalar);
            product.product_id = newId;
            return newId;
        }

        public async Task<bool> UpdateAsync(Product product, CancellationToken ct = default)
        {
            if (product is null) throw new ArgumentNullException(nameof(product));
            const string sql = @"
                UPDATE products SET category_id = @category_id, product_name = @product_name, brand = @brand,
                    price = @price, description = @description, image = @image, stock = @stock,
                    ram_gb = @ram_gb, processor = @processor, storage_gb = @storage_gb
                WHERE product_id = @id;";
            var parameters = new[]
            {
                CreateParameter("@category_id", (object?)product.category_id ?? DBNull.Value),
                CreateParameter("@product_name", product.product_name),
                CreateParameter("@brand", (object?)product.brand ?? DBNull.Value),
                CreateParameter("@price", product.price),
                CreateParameter("@description", (object?)product.description ?? DBNull.Value),
                CreateParameter("@image", (object?)product.image ?? DBNull.Value),
                CreateParameter("@stock", product.stock),
                CreateParameter("@ram_gb", (object?)product.ram_gb ?? DBNull.Value),
                CreateParameter("@processor", (object?)product.processor ?? DBNull.Value),
                CreateParameter("@storage_gb", (object?)product.storage_gb ?? DBNull.Value),
                CreateParameter("@id", product.product_id)
            };
            int rows = await ExecuteNonQueryAsync(sql, parameters, ct: ct);
            return rows > 0;
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            const string sql = "DELETE FROM products WHERE product_id = @id;";
            var parameters = new[] { CreateParameter("@id", id) };
            int rows = await ExecuteNonQueryAsync(sql, parameters, ct: ct);
            return rows > 0;
        }

        public async Task<bool> DecrementStockAsync(int productId, int quantity, CancellationToken ct = default)
        {
            const string sql = "UPDATE products SET stock = stock - @qty WHERE product_id = @id AND stock >= @qty;";
            var parameters = new[]
            {
                CreateParameter("@qty", quantity),
                CreateParameter("@id", productId)
            };
            int rowsAffected = await ExecuteNonQueryAsync(sql, parameters, ct: ct);
            return rowsAffected > 0;
        }

        private static Product MapProduct(DbDataReader reader)
        {
            int catOrdinal = reader.GetOrdinal("category_id");
            int brandOrdinal = reader.GetOrdinal("brand");
            int descOrdinal = reader.GetOrdinal("description");
            int imgOrdinal = reader.GetOrdinal("image");
            int ramOrdinal = reader.GetOrdinal("ram_gb");
            int procOrdinal = reader.GetOrdinal("processor");
            int storageOrdinal = reader.GetOrdinal("storage_gb");
            return new Product
            {
                product_id = ReadValue(reader, "product_id", 0),
                category_id = reader.IsDBNull(catOrdinal) ? null : Convert.ToInt32(reader.GetValue(catOrdinal)),
                product_name = ReadValue(reader, "product_name", string.Empty),
                brand = reader.IsDBNull(brandOrdinal) ? null : reader.GetString(brandOrdinal),
                price = ReadValue(reader, "price", 0m),
                description = reader.IsDBNull(descOrdinal) ? null : reader.GetString(descOrdinal),
                image = reader.IsDBNull(imgOrdinal) ? null : reader.GetString(imgOrdinal),
                stock = ReadValue(reader, "stock", 0),
                ram_gb = reader.IsDBNull(ramOrdinal) ? null : Convert.ToInt32(reader.GetValue(ramOrdinal)),
                processor = reader.IsDBNull(procOrdinal) ? null : reader.GetString(procOrdinal),
                storage_gb = reader.IsDBNull(storageOrdinal) ? null : Convert.ToInt32(reader.GetValue(storageOrdinal))
            };
        }
    }
}
