namespace api.ProductsModule
{
    public class Product
    {
        public int product_id { get; set; }
        public int? category_id { get; set; }
        public string product_name { get; set; } = string.Empty;
        public string? brand { get; set; }
        public decimal price { get; set; }
        public string? description { get; set; }
        public string? image { get; set; }
        public int stock { get; set; }
        public int? ram_gb { get; set; }
        public string? processor { get; set; }
        public int? storage_gb { get; set; }
    }
}
