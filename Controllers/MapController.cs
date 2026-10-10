using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "ModAccess")]
public sealed class MapController : ControllerBase
{
    private static readonly SemaphoreSlim NominatimRequestGate = new(1, 1);
    private static DateTimeOffset nextNominatimRequestAt = DateTimeOffset.MinValue;
    private static readonly TimeSpan MinimumNominatimInterval = TimeSpan.FromMilliseconds(1100);
    private static readonly TimeSpan PositiveCacheDuration = TimeSpan.FromDays(180);
    private static readonly TimeSpan NegativeCacheDuration = TimeSpan.FromHours(12);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MapController> _logger;

    public MapController(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        IConfiguration configuration,
        ILogger<MapController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Resolve a broad city/province label to an approximate map point. Do not send street addresses.
    /// Results are cached and upstream requests are serialized to respect the public Nominatim policy.
    /// </summary>
    [HttpGet("geocode")]
    [ProducesResponseType(typeof(AreaGeocodeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AreaGeocodeResponse>> GeocodeArea(
        [FromQuery(Name = "area")] string? area,
        CancellationToken cancellationToken)
    {
        var normalizedArea = string.Join(" ", (area ?? string.Empty).Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        if (normalizedArea.Length is < 2 or > 120 || normalizedArea.Any(char.IsControl))
        {
            return BadRequest(new { message = "Provide a city or province name between 2 and 120 characters." });
        }

        var cacheKey = $"osm-area-geocode:{normalizedArea.ToUpperInvariant()}";
        if (_cache.TryGetValue<GeocodeCacheEntry>(cacheKey, out var cached))
        {
            return cached?.Response is null
                ? NotFound(new { message = "No OpenStreetMap location matched this area." })
                : Ok(cached.Response);
        }

        await NominatimRequestGate.WaitAsync(cancellationToken);
        try
        {
            // Check again after entering the shared gate; another request may have filled the cache.
            if (_cache.TryGetValue<GeocodeCacheEntry>(cacheKey, out cached))
            {
                return cached?.Response is null
                    ? NotFound(new { message = "No OpenStreetMap location matched this area." })
                    : Ok(cached.Response);
            }

            var delay = nextNominatimRequestAt - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }
            nextNominatimRequestAt = DateTimeOffset.UtcNow.Add(MinimumNominatimInterval);

            var searchUrl = _configuration["OpenStreetMap:NominatimSearchUrl"]
                ?? "https://nominatim.openstreetmap.org/search";
            var requestUrl = $"{searchUrl}?format=jsonv2&limit=1&countrycodes=ph&q={Uri.EscapeDataString($"{normalizedArea}, Philippines")}";
            var client = _httpClientFactory.CreateClient("nominatim");
            using var response = await client.GetAsync(requestUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Nominatim returned HTTP {StatusCode} for an area geocode request.", (int)response.StatusCode);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Map location lookup is temporarily unavailable." });
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var places = JsonSerializer.Deserialize<List<NominatimPlace>>(json, JsonOptions) ?? [];
            var place = places.FirstOrDefault();
            AreaGeocodeResponse? result = null;
            if (place is not null
                && double.TryParse(place.Latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
                && double.TryParse(place.Longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude)
                && latitude is >= -90 and <= 90
                && longitude is >= -180 and <= 180)
            {
                result = new AreaGeocodeResponse(latitude, longitude);
            }

            var duration = result is null ? NegativeCacheDuration : PositiveCacheDuration;
            _cache.Set(cacheKey, new GeocodeCacheEntry(result), duration);
            return result is null
                ? NotFound(new { message = "No OpenStreetMap location matched this area." })
                : Ok(result);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "OpenStreetMap area lookup failed.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Map location lookup is temporarily unavailable." });
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(exception, "OpenStreetMap area lookup timed out.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Map location lookup is temporarily unavailable." });
        }
        finally
        {
            NominatimRequestGate.Release();
        }
    }

    private sealed record NominatimPlace(
        [property: JsonPropertyName("lat")] string Latitude,
        [property: JsonPropertyName("lon")] string Longitude);

    private sealed record GeocodeCacheEntry(AreaGeocodeResponse? Response);
}

public sealed record AreaGeocodeResponse(double Latitude, double Longitude);
