namespace api.StorePricingModule;

/// <summary>
/// Public storefront pricing configuration. VAT should only be enabled after VAT
/// registration/taxability and the customer-facing price convention are confirmed
/// with the BIR or an accountant.
/// </summary>
public sealed class StorePricingSettings
{
    public decimal delivery_fee_amount { get; set; }
    public bool vat_enabled { get; set; }
    public decimal vat_rate_percent { get; set; }
    public string vat_price_mode { get; set; } = "unconfirmed";
    public bool vat_delivery_taxable { get; set; }
}
