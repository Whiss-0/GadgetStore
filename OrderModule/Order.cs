namespace api.OrderModule
{
    public class Order
    {
        public int order_id { get; set; }
        public int user_id { get; set; }
        public DateTime order_date { get; set; }
        public decimal total_amount { get; set; }
        public string status { get; set; } = "Pending";
        public string? shipping_address { get; set; }
        public string? phone_number { get; set; }
        public string payment_method { get; set; } = "COD";
        public string payment_status { get; set; } = "Unpaid";
    }
}
