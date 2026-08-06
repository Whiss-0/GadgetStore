namespace api.OrderDetailModule
{
    public class OrderDetail
    {
        public int order_detail_id { get; set; }
        public int order_id { get; set; }
        public int product_id { get; set; }
        public int quantity { get; set; }
        public decimal price { get; set; }
        public decimal subtotal => quantity * price;
    }
}
