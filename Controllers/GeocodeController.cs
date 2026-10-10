using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace api.Controllers
{
    /// <summary>
    /// Admin-only endpoint that geocodes a broad Philippine locality (city/province)
    /// using Nominatim. Complies with the Nominatim usage policy:
    ///   - max 1 request/second (serialised via a semaphore + cooldown)
    ///   - stable User-Agent identifying this application
    ///   - results cached in-memory (hits never re-query Nominatim)
    ///   - only broad locality queries; raw street-level data is never forwarded
    ///   - geocoder URL is configurable via Geocoding:NominatimBaseUrl
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "ModAccess")]
    public class GeocodeController : ControllerBase
    {
        // ── Shared static cache + rate-limit gate ─────────────────────────────
        // These are static so all requests in the process share the same cache
        // and the same 1-req/s upstream budget, regardless of controller scope.
        private static readonly ConcurrentDictionary<string, GeoResult?> _cache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly SemaphoreSlim _gate = new(1, 1);
        private static DateTime _lastRequest = DateTime.MinValue;
        private static readonly TimeSpan _minGap = TimeSpan.FromSeconds(1);

        // Lazily created so the IConfiguration is available at first call
        private static HttpClient? _http;
        private static readonly object _httpLock = new();

        private readonly IConfiguration _config;
        private readonly ILogger<GeocodeController> _logger;

        public GeocodeController(IConfiguration config, ILogger<GeocodeController> logger)
        {
            _config = config;
            _logger = logger;
        }

        /// <summary>GET /api/geocode?area=Olongapo,Zambales</summary>
        [HttpGet]
        public async Task<IActionResult> Geocode([FromQuery] string? area, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(area))
                return BadRequest(new { message = "area query parameter is required." });

            // Safety: reject anything that looks like a street-level address
            // (contains a digit followed by a space, indicating a house number).
            if (Regex.IsMatch(area, @"^\d+\s"))
                return BadRequest(new { message = "Only city/province queries are accepted." });

            var cacheKey = area.Trim();

            // Return cached result immediately (including cached "not found")
            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                if (cached == null) return NotFound(new { message = "Location not found." });
                return Ok(cached);
            }

            // Serialize upstream Nominatim requests: 1 req/sec maximum
            await _gate.WaitAsync(ct);
            try
            {
                // Double-check after acquiring the lock (another request may have populated cache)
                if (_cache.TryGetValue(cacheKey, out cached))
                {
                    if (cached == null) return NotFound(new { message = "Location not found." });
                    return Ok(cached);
                }

                // Rate-limit cooldown
                var elapsed = DateTime.UtcNow - _lastRequest;
                if (elapsed < _minGap)
                {
                    await Task.Delay(_minGap - elapsed, ct);
                }

                var result = await QueryNominatimAsync(cacheKey, ct);
                _cache[cacheKey] = result;   // cache hit or explicit null for not-found
                _lastRequest = DateTime.UtcNow;

                if (result == null) return NotFound(new { message = "Location not found." });
                return Ok(result);
            }
            catch (OperationCanceledException)
            {
                return StatusCode(503, new { message = "Geocoding request was cancelled." });
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Geocoding upstream error for area (details omitted): {Type}", ex.GetType().Name);
                return StatusCode(502, new { message = "Geocoder temporarily unavailable." });
            }
            finally
            {
                _gate.Release();
            }
        }

        // ── Nominatim query ───────────────────────────────────────────────────
        private async Task<GeoResult?> QueryNominatimAsync(string area, CancellationToken ct)
        {
            var http = GetOrCreateHttpClient();
            var baseUrl = _config["Geocoding:NominatimBaseUrl"] ?? "https://nominatim.openstreetmap.org";
            // Restrict to Philippines (countrycodes=ph) to reduce mis-geocoding
            var url = $"{baseUrl.TrimEnd('/')}/search?q={Uri.EscapeDataString(area)}, Philippines&countrycodes=ph&format=json&limit=1&addressdetails=0";

            // Nominatim policy: do not log raw query data
            _logger.LogDebug("Geocoding query issued (area suppressed from logs)");

            using var response = await http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var arr = doc.RootElement;

            if (arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0)
                return null;

            var first = arr[0];
            if (!first.TryGetProperty("lat", out var latEl) || !first.TryGetProperty("lon", out var lonEl))
                return null;

            if (!double.TryParse(latEl.GetString(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double lat) ||
                !double.TryParse(lonEl.GetString(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double lon))
                return null;

            return new GeoResult { Lat = lat, Lon = lon };
        }

        private HttpClient GetOrCreateHttpClient()
        {
            if (_http != null) return _http;
            lock (_httpLock)
            {
                if (_http != null) return _http;
                _http = new HttpClient();
                // Nominatim policy: identify the application with a stable User-Agent
                _http.DefaultRequestHeaders.UserAgent.ParseAdd("GadgetStore-Admin/1.0 (contact: admin@gadgetstore.local)");
                _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                _http.Timeout = TimeSpan.FromSeconds(10);
            }
            return _http;
        }
    }

    public sealed class GeoResult
    {
        public double Lat { get; set; }
        public double Lon { get; set; }
    }
}
