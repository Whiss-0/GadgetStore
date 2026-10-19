using System.ComponentModel.DataAnnotations;
using api.StorePricingModule;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace api.Controllers;

[ApiController]
[Route("api/store-pricing")]
public sealed class StorePricingController : ControllerBase
{
    private readonly IStorePricingSettingsRepository _repository;

    public StorePricingController(IStorePricingSettingsRepository repository) => _repository = repository;

    /// <summary>Returns the public storefront fee and VAT display configuration.</summary>
    [AllowAnonymous]
    [HttpGet]
    public async Task<ActionResult<StorePricingSettings>> Get(CancellationToken ct)
    {
        var settings = await _repository.GetAsync(ct);
        if (settings is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "Store pricing settings are not initialized. Apply the one-time database migration." });
        return Ok(settings);
    }

    /// <summary>
    /// Updates public storefront fee and VAT configuration. VAT must remain disabled
    /// until VAT registration/taxability and the price convention have been confirmed
    /// with the BIR or an accountant.
    /// </summary>
    [Authorize(Policy = "AdminAccess")]
    [HttpPut]
    public async Task<ActionResult<StorePricingSettings>> Update([FromBody] UpdateStorePricingRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (request.VatEnabled && request.VatPriceMode == "unconfirmed")
            return BadRequest(new { message = "VAT cannot be enabled while VatPriceMode is unconfirmed. Confirm VAT registration/taxability and the price convention with the BIR or an accountant first." });

        var settings = new StorePricingSettings
        {
            delivery_fee_amount = request.DeliveryFeeAmount,
            vat_enabled = request.VatEnabled,
            vat_rate_percent = request.VatRatePercent,
            vat_price_mode = request.VatPriceMode,
            vat_delivery_taxable = request.VatDeliveryTaxable,
        };

        if (!await _repository.UpdateAsync(settings, ct))
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "Store pricing settings are not initialized. Apply the one-time database migration." });

        return Ok(settings);
    }

    /// <summary>Quotes the authoritative area fee after verifying the province/city relationship.</summary>
    [AllowAnonymous]
    [HttpGet("delivery-fees/quote")]
    public async Task<ActionResult<DeliveryFeeQuote>> QuoteDeliveryFee(
        [FromQuery] string? provinceCode, [FromQuery] string? cityCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(provinceCode) || string.IsNullOrWhiteSpace(cityCode))
            return BadRequest(new { message = "Both provinceCode and cityCode are required." });
        try
        {
            return Ok(await _repository.GetDeliveryFeeQuoteAsync(provinceCode, cityCode, ct));
        }
        catch (InvalidAreaCodeException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (StorePricingNotInitializedException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ex.Message });
        }
    }

    /// <summary>Lists all configured province-wide and city/municipality fee rules.</summary>
    [Authorize(Policy = "AdminAccess")]
    [HttpGet("delivery-fees")]
    public async Task<ActionResult<List<DeliveryFeeRule>>> GetDeliveryFeeRules(CancellationToken ct) =>
        Ok(await _repository.GetDeliveryFeeRulesAsync(ct));

    /// <summary>Creates a province-wide rule (city_code null) or a city-specific rule.</summary>
    [Authorize(Policy = "AdminAccess")]
    [HttpPost("delivery-fees")]
    public async Task<ActionResult<DeliveryFeeRule>> CreateDeliveryFeeRule(
        [FromBody] CreateDeliveryFeeRuleRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            var rule = await _repository.CreateDeliveryFeeRuleAsync(
                request.province_code, request.city_code, request.delivery_fee_amount, ct);
            if (rule is null)
                return Conflict(new { message = "A delivery fee rule already exists for this province/city scope." });
            return Created($"/api/store-pricing/delivery-fees/{rule.rule_id}", rule);
        }
        catch (InvalidAreaCodeException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return Conflict(new { message = "A delivery fee rule already exists for this province/city scope." });
        }
    }

    /// <summary>Changes the fee amount for one existing rule.</summary>
    [Authorize(Policy = "AdminAccess")]
    [HttpPut("delivery-fees/{ruleId:int}")]
    public async Task<IActionResult> UpdateDeliveryFeeRule(
        int ruleId, [FromBody] UpdateDeliveryFeeRuleRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        return await _repository.UpdateDeliveryFeeRuleAsync(ruleId, request.delivery_fee_amount, ct)
            ? NoContent()
            : NotFound(new { message = $"Delivery fee rule {ruleId} was not found." });
    }

    /// <summary>Removes one existing rule; the global fallback remains unaffected.</summary>
    [Authorize(Policy = "AdminAccess")]
    [HttpDelete("delivery-fees/{ruleId:int}")]
    public async Task<IActionResult> DeleteDeliveryFeeRule(int ruleId, CancellationToken ct) =>
        await _repository.DeleteDeliveryFeeRuleAsync(ruleId, ct)
            ? NoContent()
            : NotFound(new { message = $"Delivery fee rule {ruleId} was not found." });
}

public sealed class UpdateStorePricingRequest
{
    [JsonPropertyName("delivery_fee_amount")]
    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal DeliveryFeeAmount { get; set; }

    [JsonPropertyName("vat_enabled")]
    public bool VatEnabled { get; set; }

    [JsonPropertyName("vat_rate_percent")]
    [Range(typeof(decimal), "0", "100")]
    public decimal VatRatePercent { get; set; } = 12m;

    [JsonPropertyName("vat_price_mode")]
    [Required]
    [RegularExpression("^(unconfirmed|inclusive|exclusive)$",
        ErrorMessage = "VatPriceMode must be unconfirmed, inclusive, or exclusive.")]
    public string VatPriceMode { get; set; } = "unconfirmed";

    [JsonPropertyName("vat_delivery_taxable")]
    public bool VatDeliveryTaxable { get; set; }
}

public sealed class CreateDeliveryFeeRuleRequest
{
    [Required]
    [JsonPropertyName("province_code")]
    [StringLength(4, MinimumLength = 4)]
    public string province_code { get; set; } = string.Empty;

    [JsonPropertyName("city_code")]
    [StringLength(6, MinimumLength = 6)]
    public string? city_code { get; set; }

    [JsonPropertyName("delivery_fee_amount")]
    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal delivery_fee_amount { get; set; }
}

public sealed class UpdateDeliveryFeeRuleRequest
{
    [JsonPropertyName("delivery_fee_amount")]
    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal delivery_fee_amount { get; set; }
}
