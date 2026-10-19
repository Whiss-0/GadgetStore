namespace api.StorePricingModule;

public interface IStorePricingSettingsRepository
{
    Task<StorePricingSettings?> GetAsync(CancellationToken ct = default);
    Task<bool> UpdateAsync(StorePricingSettings settings, CancellationToken ct = default);
    Task<DeliveryFeeQuote> GetDeliveryFeeQuoteAsync(string provinceCode, string cityCode, CancellationToken ct = default);
    Task<List<DeliveryFeeRule>> GetDeliveryFeeRulesAsync(CancellationToken ct = default);
    Task<DeliveryFeeRule?> CreateDeliveryFeeRuleAsync(string provinceCode, string? cityCode, decimal feeAmount, CancellationToken ct = default);
    Task<bool> UpdateDeliveryFeeRuleAsync(int ruleId, decimal feeAmount, CancellationToken ct = default);
    Task<bool> DeleteDeliveryFeeRuleAsync(int ruleId, CancellationToken ct = default);
}
