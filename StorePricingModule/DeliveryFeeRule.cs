namespace api.StorePricingModule;

/// <summary>A configured delivery fee at either province or city/municipality scope.</summary>
public sealed class DeliveryFeeRule
{
    public int rule_id { get; set; }
    public string province_code { get; set; } = string.Empty;
    public string province_name { get; set; } = string.Empty;
    public string? city_code { get; set; }
    public string? city_name { get; set; }
    public decimal delivery_fee_amount { get; set; }
    public string scope => city_code is null ? "province" : "city";
}

/// <summary>Resolved quote for a validated province and its city/municipality.</summary>
public sealed class DeliveryFeeQuote
{
    public string province_code { get; set; } = string.Empty;
    public string province_name { get; set; } = string.Empty;
    public string city_code { get; set; } = string.Empty;
    public string city_name { get; set; } = string.Empty;
    public decimal delivery_fee_amount { get; set; }
    public string fee_scope { get; set; } = "default";
}
