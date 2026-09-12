using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Ekom.Shipping.DhlLocationFinder;

internal sealed class DhlLocationFinderService : IDhlLocationFinderService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DhlLocationFinderOptions _options;

    public DhlLocationFinderService(
        IHttpClientFactory httpClientFactory,
        IOptions<DhlLocationFinderOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<DhlLocation>> FindByAddressAsync(
        string accountReference,
        DhlLocationAddressSearch request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CountryCode);
        var query = CommonQuery(
            request.ProviderTypes,
            request.LocationTypes,
            request.ServiceTypes,
            request.Radius,
            request.Limit,
            request.HideClosedLocations,
            request.CurrentDate);
        query.InsertRange(0, [
            new("countryCode", request.CountryCode),
            new("addressLocality", request.AddressLocality),
            new("postalCode", request.PostalCode),
            new("streetAddress", request.StreetAddress),
        ]);
        var response = await GetAsync<DhlLocationsResponse>(accountReference, "find-by-address", query, cancellationToken)
            .ConfigureAwait(false);
        return response?.Locations ?? [];
    }

    public async Task<IReadOnlyList<DhlLocation>> FindByGeoAsync(
        string accountReference,
        DhlLocationGeoSearch request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Latitude is < -90 or > 90)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Latitude must be between -90 and 90.");
        }

        if (request.Longitude is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Longitude must be between -180 and 180.");
        }

        var query = CommonQuery(
            request.ProviderTypes,
            request.LocationTypes,
            request.ServiceTypes,
            request.Radius,
            request.Limit,
            request.HideClosedLocations,
            request.CurrentDate);
        query.InsertRange(0, [
            new("latitude", request.Latitude.ToString(CultureInfo.InvariantCulture)),
            new("longitude", request.Longitude.ToString(CultureInfo.InvariantCulture)),
            new("countryCode", request.CountryCode),
        ]);
        var response = await GetAsync<DhlLocationsResponse>(accountReference, "find-by-geo", query, cancellationToken)
            .ConfigureAwait(false);
        return response?.Locations ?? [];
    }

    public Task<DhlLocation> FindByKeywordIdAsync(
        string accountReference,
        string keywordId,
        string countryCode,
        string postalCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keywordId);
        ArgumentException.ThrowIfNullOrWhiteSpace(countryCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(postalCode);
        return GetRequiredLocationAsync(accountReference, "find-by-keyword-id", [
            new("keywordId", keywordId),
            new("countryCode", countryCode),
            new("postalCode", postalCode),
        ], cancellationToken);
    }

    public Task<DhlLocation> GetLocationAsync(
        string accountReference,
        string locationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locationId);
        return GetRequiredLocationAsync(
            accountReference,
            $"locations/{Uri.EscapeDataString(locationId)}",
            [],
            cancellationToken);
    }

    private async Task<DhlLocation> GetRequiredLocationAsync(
        string accountReference,
        string path,
        IReadOnlyList<KeyValuePair<string, string?>> query,
        CancellationToken cancellationToken) =>
        await GetAsync<DhlLocation>(accountReference, path, query, cancellationToken).ConfigureAwait(false)
        ?? throw new ShippingProviderException("dhl-location-finder", "DHL returned an empty location response.");

    private async Task<T?> GetAsync<T>(
        string accountReference,
        string path,
        IReadOnlyList<KeyValuePair<string, string?>> query,
        CancellationToken cancellationToken)
    {
        var account = GetAccount(accountReference);
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(account.ApiUrl, path, query));
        request.Headers.Add("DHL-API-Key", account.ApiKey);
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await _httpClientFactory.CreateClient(nameof(DhlLocationFinderService))
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ShippingProviderException(
                "dhl-location-finder",
                $"DHL Location Finder failed with HTTP status {(int)response.StatusCode}.");
        }

        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new ShippingProviderException("dhl-location-finder", "DHL returned invalid location data.", exception);
        }
    }

    private DhlLocationFinderAccountOptions GetAccount(string accountReference)
    {
        if (string.IsNullOrWhiteSpace(accountReference) ||
            !_options.Accounts.TryGetValue(accountReference, out var account))
        {
            throw new ShippingConfigurationException($"DHL Location Finder account '{accountReference}' is not configured.");
        }

        return account;
    }

    private static List<KeyValuePair<string, string?>> CommonQuery(
        IReadOnlyList<string>? providers,
        IReadOnlyList<string>? locations,
        IReadOnlyList<string>? services,
        decimal? radius,
        decimal? limit,
        bool? hideClosed,
        string? currentDate)
    {
        if (radius is < 0 or > 1_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(radius));
        }

        if (limit is < 0 or > 50)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        var query = new List<KeyValuePair<string, string?>>();
        AddMany(query, "providerType", providers);
        AddMany(query, "locationType", locations);
        AddMany(query, "serviceType", services);
        query.Add(new("radius", radius?.ToString(CultureInfo.InvariantCulture)));
        query.Add(new("limit", limit?.ToString(CultureInfo.InvariantCulture)));
        query.Add(new("hideClosedLocations", hideClosed?.ToString().ToLowerInvariant()));
        query.Add(new("currentDate", currentDate));
        return query;
    }

    private static void AddMany(
        ICollection<KeyValuePair<string, string?>> query,
        string key,
        IReadOnlyList<string>? values)
    {
        if (values is null)
        {
            return;
        }

        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            query.Add(new(key, value));
        }
    }

    private static Uri BuildUri(
        string apiUrl,
        string path,
        IEnumerable<KeyValuePair<string, string?>> query)
    {
        var baseUri = new Uri(apiUrl.EndsWith("/", StringComparison.Ordinal) ? apiUrl : apiUrl + '/');
        var values = query
            .Where(value => !string.IsNullOrWhiteSpace(value.Value))
            .Select(value => $"{Uri.EscapeDataString(value.Key)}={Uri.EscapeDataString(value.Value!)}");
        var suffix = string.Join('&', values);
        return new Uri(baseUri, string.IsNullOrEmpty(suffix) ? path : $"{path}?{suffix}");
    }

    private sealed class DhlLocationsResponse
    {
        public IReadOnlyList<DhlLocation>? Locations { get; init; }
    }
}
