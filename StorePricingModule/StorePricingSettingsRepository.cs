using System.Data.Common;
using api.Main;

namespace api.StorePricingModule;

public sealed class StorePricingSettingsRepository : BaseRepository, IStorePricingSettingsRepository
{
    public StorePricingSettingsRepository(MyCon dbConnection) : base(dbConnection) { }

    public async Task<StorePricingSettings?> GetAsync(CancellationToken ct = default)
    {
        const string sql = @"
            SELECT delivery_fee_amount, vat_enabled, vat_rate_percent, vat_price_mode, vat_delivery_taxable
            FROM store_pricing_settings
            WHERE id = 1
            LIMIT 1;";
        var rows = await ExecuteReaderToListAsync(sql, Map, ct: ct);
        return rows.Count == 0 ? null : rows[0];
    }

    public async Task<bool> UpdateAsync(StorePricingSettings settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        const string sql = @"
            UPDATE store_pricing_settings
            SET delivery_fee_amount = @delivery_fee_amount,
                vat_enabled = @vat_enabled,
                vat_rate_percent = @vat_rate_percent,
                vat_price_mode = @vat_price_mode,
                vat_delivery_taxable = @vat_delivery_taxable
            WHERE id = 1;";
        var parameters = new[]
        {
            CreateParameter("@delivery_fee_amount", settings.delivery_fee_amount),
            CreateParameter("@vat_enabled", settings.vat_enabled ? 1 : 0),
            CreateParameter("@vat_rate_percent", settings.vat_rate_percent),
            CreateParameter("@vat_price_mode", settings.vat_price_mode),
            CreateParameter("@vat_delivery_taxable", settings.vat_delivery_taxable ? 1 : 0),
        };
        return await ExecuteNonQueryAsync(sql, parameters, ct: ct) > 0;
    }

    public async Task<DeliveryFeeQuote> GetDeliveryFeeQuoteAsync(string provinceCode, string cityCode, CancellationToken ct = default)
    {
        const string settingsSql = "SELECT delivery_fee_amount FROM store_pricing_settings WHERE id = 1 LIMIT 1;";
        var fallbackRows = await ExecuteReaderToListAsync(settingsSql,
            r => ReadValue(r, "delivery_fee_amount", 0m), ct: ct);
        if (fallbackRows.Count == 0)
            throw new StorePricingNotInitializedException("Store pricing settings are not initialized. Apply the appropriate database migration.");

        const string sql = @"
            SELECT p.province_code, p.province_name, c.city_code, c.city_name,
                CASE
                    WHEN EXISTS (SELECT 1 FROM delivery_fee_rules cr
                                 WHERE cr.province_code = p.province_code AND cr.city_code = c.city_code)
                    THEN (SELECT cr.delivery_fee_amount FROM delivery_fee_rules cr
                          WHERE cr.province_code = p.province_code AND cr.city_code = c.city_code LIMIT 1)
                    WHEN EXISTS (SELECT 1 FROM delivery_fee_rules pr
                                 WHERE pr.province_code = p.province_code AND pr.city_code IS NULL)
                    THEN (SELECT pr.delivery_fee_amount FROM delivery_fee_rules pr
                          WHERE pr.province_code = p.province_code AND pr.city_code IS NULL LIMIT 1)
                    ELSE @fallback_fee
                END AS delivery_fee_amount,
                CASE
                    WHEN EXISTS (SELECT 1 FROM delivery_fee_rules cr
                                 WHERE cr.province_code = p.province_code AND cr.city_code = c.city_code)
                    THEN 'city'
                    WHEN EXISTS (SELECT 1 FROM delivery_fee_rules pr
                                 WHERE pr.province_code = p.province_code AND pr.city_code IS NULL)
                    THEN 'province'
                    ELSE 'default'
                END AS fee_scope
            FROM ph_provinces p
            JOIN ph_cities c ON c.province_code = p.province_code
            WHERE p.province_code = @province_code AND c.city_code = @city_code
            LIMIT 1;";
        var rows = await ExecuteReaderToListAsync(sql, r => new DeliveryFeeQuote
        {
            province_code = ReadValue(r, "province_code", string.Empty),
            province_name = ReadValue(r, "province_name", string.Empty),
            city_code = ReadValue(r, "city_code", string.Empty),
            city_name = ReadValue(r, "city_name", string.Empty),
            delivery_fee_amount = ReadValue(r, "delivery_fee_amount", 0m),
            fee_scope = ReadValue(r, "fee_scope", "default"),
        }, new[]
        {
            CreateParameter("@province_code", provinceCode),
            CreateParameter("@city_code", cityCode),
            CreateParameter("@fallback_fee", fallbackRows[0]),
        }, ct: ct);
        if (rows.Count == 0)
            throw new InvalidAreaCodeException("The supplied provinceCode and cityCode are invalid or the city/municipality does not belong to that province.");
        return rows[0];
    }

    public Task<List<DeliveryFeeRule>> GetDeliveryFeeRulesAsync(CancellationToken ct = default)
    {
        const string sql = @"
            SELECT r.rule_id, r.province_code, p.province_name, r.city_code, c.city_name,
                   r.delivery_fee_amount
            FROM delivery_fee_rules r
            JOIN ph_provinces p ON p.province_code = r.province_code
            LEFT JOIN ph_cities c ON c.city_code = r.city_code AND c.province_code = r.province_code
            ORDER BY r.province_code, r.city_code IS NULL, r.city_code, r.rule_id;";
        return ExecuteReaderToListAsync(sql, MapRule, ct: ct);
    }

    public async Task<DeliveryFeeRule?> CreateDeliveryFeeRuleAsync(string provinceCode, string? cityCode, decimal feeAmount, CancellationToken ct = default)
    {
        await ValidateProvinceAsync(provinceCode, ct);
        if (cityCode is not null)
            await ValidateCityBelongsToProvinceAsync(provinceCode, cityCode, ct);

        const string duplicateSql = @"
            SELECT COUNT(*) FROM delivery_fee_rules
            WHERE province_code = @province_code
              AND ((city_code IS NULL AND @city_code IS NULL) OR city_code = @city_code);";
        var existing = await ExecuteScalarAsync<long>(duplicateSql, new[]
        {
            CreateParameter("@province_code", provinceCode),
            CreateParameter("@city_code", (object?)cityCode ?? DBNull.Value),
        }, ct: ct);
        if (existing > 0) return null;

        const string insertSql = @"
            INSERT INTO delivery_fee_rules (province_code, city_code, delivery_fee_amount)
            VALUES (@province_code, @city_code, @delivery_fee_amount);
            SELECT last_insert_rowid();";
        var id = await ExecuteScalarAsync<long>(insertSql, new[]
        {
            CreateParameter("@province_code", provinceCode),
            CreateParameter("@city_code", (object?)cityCode ?? DBNull.Value),
            CreateParameter("@delivery_fee_amount", feeAmount),
        }, ct: ct);
        const string selectSql = @"
            SELECT r.rule_id, r.province_code, p.province_name, r.city_code, c.city_name,
                   r.delivery_fee_amount
            FROM delivery_fee_rules r
            JOIN ph_provinces p ON p.province_code = r.province_code
            LEFT JOIN ph_cities c ON c.city_code = r.city_code AND c.province_code = r.province_code
            WHERE r.rule_id = @rule_id LIMIT 1;";
        var rows = await ExecuteReaderToListAsync(selectSql, MapRule,
            new[] { CreateParameter("@rule_id", id) }, ct: ct);
        return rows.Count == 0 ? null : rows[0];
    }

    public async Task<bool> UpdateDeliveryFeeRuleAsync(int ruleId, decimal feeAmount, CancellationToken ct = default)
    {
        const string sql = "UPDATE delivery_fee_rules SET delivery_fee_amount = @fee WHERE rule_id = @id;";
        return await ExecuteNonQueryAsync(sql, new[]
        {
            CreateParameter("@fee", feeAmount),
            CreateParameter("@id", ruleId),
        }, ct: ct) > 0;
    }

    public async Task<bool> DeleteDeliveryFeeRuleAsync(int ruleId, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM delivery_fee_rules WHERE rule_id = @id;";
        return await ExecuteNonQueryAsync(sql, new[] { CreateParameter("@id", ruleId) }, ct: ct) > 0;
    }

    private async Task ValidateProvinceAsync(string provinceCode, CancellationToken ct)
    {
        const string sql = "SELECT COUNT(*) FROM ph_provinces WHERE province_code = @province_code;";
        if (await ExecuteScalarAsync<long>(sql, new[] { CreateParameter("@province_code", provinceCode) }, ct: ct) == 0)
            throw new InvalidAreaCodeException("province_code is not a recognized Philippine province code.");
    }

    private async Task ValidateCityBelongsToProvinceAsync(string provinceCode, string cityCode, CancellationToken ct)
    {
        const string sql = "SELECT COUNT(*) FROM ph_cities WHERE city_code = @city_code AND province_code = @province_code;";
        if (await ExecuteScalarAsync<long>(sql, new[]
        {
            CreateParameter("@city_code", cityCode),
            CreateParameter("@province_code", provinceCode),
        }, ct: ct) == 0)
            throw new InvalidAreaCodeException("city_code is not a recognized city/municipality in the supplied province.");
    }

    private static DeliveryFeeRule MapRule(DbDataReader reader) => new()
    {
        rule_id = ReadValue(reader, "rule_id", 0),
        province_code = ReadValue(reader, "province_code", string.Empty),
        province_name = ReadValue(reader, "province_name", string.Empty),
        city_code = ReadValue<string?>(reader, "city_code", null),
        city_name = ReadValue<string?>(reader, "city_name", null),
        delivery_fee_amount = ReadValue(reader, "delivery_fee_amount", 0m),
    };

    private static StorePricingSettings Map(DbDataReader reader) => new()
    {
        delivery_fee_amount = ReadValue(reader, "delivery_fee_amount", 0m),
        vat_enabled = ReadValue(reader, "vat_enabled", 0) != 0,
        vat_rate_percent = ReadValue(reader, "vat_rate_percent", 12m),
        vat_price_mode = ReadValue(reader, "vat_price_mode", "unconfirmed"),
        vat_delivery_taxable = ReadValue(reader, "vat_delivery_taxable", 0) != 0,
    };
}

public sealed class InvalidAreaCodeException : InvalidOperationException
{
    public InvalidAreaCodeException(string message) : base(message) { }
}

public sealed class StorePricingNotInitializedException : InvalidOperationException
{
    public StorePricingNotInitializedException(string message) : base(message) { }
}
