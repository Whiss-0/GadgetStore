using System.Data.Common;
using api.Main;

namespace api.ReviewModule
{
    public class ReviewRepository : BaseRepository, IReviewRepository
    {
        public ReviewRepository(MyCon dbConnection) : base(dbConnection) { }

        private const string SelectCols = "review_id, user_id, product_id, rating, comment, review_date";

        public async Task<Review?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            var sql = $"SELECT {SelectCols} FROM reviews WHERE review_id = @id LIMIT 1;";
            var parameters = new[] { CreateParameter("@id", id) };
            var list = await ExecuteReaderToListAsync(sql, MapReview, parameters, ct: ct);
            return list.Count > 0 ? list[0] : null;
        }

        public async Task<List<Review>> GetByProductAsync(int productId, CancellationToken ct = default)
        {
            var sql = $"SELECT {SelectCols} FROM reviews WHERE product_id = @productId ORDER BY review_date DESC;";
            var parameters = new[] { CreateParameter("@productId", productId) };
            return await ExecuteReaderToListAsync(sql, MapReview, parameters, ct: ct);
        }

        public async Task<List<Review>> GetByUserAsync(int userId, CancellationToken ct = default)
        {
            var sql = $"SELECT {SelectCols} FROM reviews WHERE user_id = @userId ORDER BY review_date DESC;";
            var parameters = new[] { CreateParameter("@userId", userId) };
            return await ExecuteReaderToListAsync(sql, MapReview, parameters, ct: ct);
        }

        public async Task<List<Review>> GetAllAsync(CancellationToken ct = default)
        {
            var sql = $"SELECT {SelectCols} FROM reviews ORDER BY review_date DESC;";
            return await ExecuteReaderToListAsync(sql, MapReview, ct: ct);
        }

        public async Task<int> CreateAsync(Review review, CancellationToken ct = default)
        {
            if (review is null) throw new ArgumentNullException(nameof(review));
            const string sql = @"
                INSERT INTO reviews (user_id, product_id, rating, comment, review_date)
                VALUES (@user_id, @product_id, @rating, @comment, @review_date);
                SELECT last_insert_rowid();";
            var parameters = new[]
            {
                CreateParameter("@user_id", review.user_id),
                CreateParameter("@product_id", review.product_id),
                CreateParameter("@rating", review.rating),
                CreateParameter("@comment", (object?)review.comment ?? DBNull.Value),
                CreateParameter("@review_date", review.review_date.ToString("yyyy-MM-dd HH:mm:ss"))
            };
            var newIdScalar = await ExecuteScalarAsync<long>(sql, parameters, ct: ct);
            int newId = Convert.ToInt32(newIdScalar);
            review.review_id = newId;
            return newId;
        }

        public async Task<bool> UpdateAsync(Review review, CancellationToken ct = default)
        {
            if (review is null) throw new ArgumentNullException(nameof(review));
            const string sql = "UPDATE reviews SET rating = @rating, comment = @comment WHERE review_id = @id;";
            var parameters = new[]
            {
                CreateParameter("@rating", review.rating),
                CreateParameter("@comment", (object?)review.comment ?? DBNull.Value),
                CreateParameter("@id", review.review_id)
            };
            int rows = await ExecuteNonQueryAsync(sql, parameters, ct: ct);
            return rows > 0;
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            const string sql = "DELETE FROM reviews WHERE review_id = @id;";
            var parameters = new[] { CreateParameter("@id", id) };
            int rows = await ExecuteNonQueryAsync(sql, parameters, ct: ct);
            return rows > 0;
        }

        private static Review MapReview(DbDataReader reader)
        {
            int commentOrdinal = reader.GetOrdinal("comment");
            return new Review
            {
                review_id = ReadValue(reader, "review_id", 0),
                user_id = ReadValue(reader, "user_id", 0),
                product_id = ReadValue(reader, "product_id", 0),
                rating = ReadValue(reader, "rating", 0),
                comment = reader.IsDBNull(commentOrdinal) ? null : reader.GetString(commentOrdinal),
                review_date = Convert.ToDateTime(reader.GetValue(reader.GetOrdinal("review_date")))
            };
        }
    }
}
