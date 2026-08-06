namespace api.ReviewModule
{
    public class Review
    {
        public int review_id { get; set; }
        public int user_id { get; set; }
        public int product_id { get; set; }
        public int rating { get; set; } // 1-5
        public string? comment { get; set; }
        public DateTime review_date { get; set; }
    }
}
