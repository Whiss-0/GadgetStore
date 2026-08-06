using System.Data.Common;
using api.Main;

namespace api.CategoriesModule
{
    public class CategoriesRepository : BaseRepository, ICategoriesRepository
    {
        public CategoriesRepository(MyCon dbConnection) : base(dbConnection) { }

        public async Task<Category?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            const string sql = "SELECT category_id, category_name FROM categories WHERE category_id = @id LIMIT 1;";
            var parameters = new[] { CreateParameter("@id", id) };
            var list = await ExecuteReaderToListAsync(sql, MapCategory, parameters, ct: ct);
            return list.Count > 0 ? list[0] : null;
        }

        public async Task<List<Category>> GetAllAsync(CancellationToken ct = default)
        {
            const string sql = "SELECT category_id, category_name FROM categories;";
            return await ExecuteReaderToListAsync(sql, MapCategory, ct: ct);
        }

        public async Task<PaginationModel<Category>> GetPagedAsync(int pageNumber, int pageSize, CancellationToken ct = default)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 10;
            int offset = (pageNumber - 1) * pageSize;

            var totalCountScalar = await ExecuteScalarAsync<long>("SELECT COUNT(*) FROM categories;", ct: ct);
            int totalCount = Convert.ToInt32(totalCountScalar);

            const string sql = "SELECT category_id, category_name FROM categories LIMIT @pageSize OFFSET @offset;";
            var parameters = new[] { CreateParameter("@pageSize", pageSize), CreateParameter("@offset", offset) };
            var items = await ExecuteReaderToListAsync(sql, MapCategory, parameters, ct: ct);

            return new PaginationModel<Category> { Items = items, TotalCount = totalCount, PageSize = pageSize, CurrentPage = pageNumber };
        }

        public async Task<int> CreateAsync(Category category, CancellationToken ct = default)
        {
            if (category is null) throw new ArgumentNullException(nameof(category));
            const string sql = @"
                INSERT INTO categories (category_name) VALUES (@category_name);
                SELECT last_insert_rowid();";
            var parameters = new[] { CreateParameter("@category_name", category.category_name) };
            var newIdScalar = await ExecuteScalarAsync<long>(sql, parameters, ct: ct);
            int newId = Convert.ToInt32(newIdScalar);
            category.category_id = newId;
            return newId;
        }

        public async Task<bool> UpdateAsync(Category category, CancellationToken ct = default)
        {
            if (category is null) throw new ArgumentNullException(nameof(category));
            const string sql = "UPDATE categories SET category_name = @category_name WHERE category_id = @id;";
            var parameters = new[] { CreateParameter("@category_name", category.category_name), CreateParameter("@id", category.category_id) };
            int rows = await ExecuteNonQueryAsync(sql, parameters, ct: ct);
            return rows > 0;
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            const string sql = "DELETE FROM categories WHERE category_id = @id;";
            var parameters = new[] { CreateParameter("@id", id) };
            int rows = await ExecuteNonQueryAsync(sql, parameters, ct: ct);
            return rows > 0;
        }

        private static Category MapCategory(DbDataReader reader) => new Category
        {
            category_id = ReadValue(reader, "category_id", 0),
            category_name = ReadValue(reader, "category_name", string.Empty)
        };
    }
}
