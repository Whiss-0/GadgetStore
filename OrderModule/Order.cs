namespace api.OrderModule
{
    public class Order
    {
        public int order_id { get; set; }
        public int user_id { get; set; }
        public DateTime order_date { get; set; }
        public decimal total_amount { get; set; }
        public decimal subtotal_amount { get; set; }
        public decimal delivery_fee_amount { get; set; }
        public decimal vat_amount { get; set; }
        public decimal vat_rate_percent { get; set; }
        public string vat_price_mode { get; set; } = "disabled";
        public string status { get; set; } = "Pending";
        public string? shipping_address { get; set; }
        public string? shipping_city_code { get; set; }
        public string? shipping_province_code { get; set; }
        public string? phone_number { get; set; }
        public string payment_method { get; set; } = "COD";
        public string payment_status { get; set; } = "Unpaid";
        public string? courier_name { get; set; }
        public string? tracking_number { get; set; }
        public string? estimated_delivery_date { get; set; }
    }
}
