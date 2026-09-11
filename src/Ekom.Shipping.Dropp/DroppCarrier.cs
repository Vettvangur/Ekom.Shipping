using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ekom.Shipping.Dropp;

internal sealed class DroppCarrier : IShippingCarrier
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<DroppCarrier> _logger;
    private readonly DroppOptions _options;

    public DroppCarrier(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        IOptions<DroppOptions> options,
        ILogger<DroppCarrier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public string Alias => DroppShippingDefaults.CarrierAlias;

    public ShippingCarrierCapabilities Capabilities =>
        ShippingCarrierCapabilities.Services | ShippingCarrierCapabilities.PickupLocations;

    public Task<IReadOnlyList<ShippingService>> GetServicesAsync(
        string accountReference,
        ShippingLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        _ = GetAccount(accountReference);
        IReadOnlyList<ShippingService> services =
        [
            new ShippingService(DroppShippingDefaults.PickupServiceId, "Dropp pickup", true),
        ];
        return Task.FromResult(services);
    }

    public async Task<IReadOnlyList<PickupLocation>> GetPickupLocationsAsync(
        string accountReference,
        string serviceId,
        ShippingLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(serviceId, DroppShippingDefaults.PickupServiceId, StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<PickupLocation>();
        }

        var account = GetAccount(accountReference);
        var cacheKey = $"ekom-shipping:dropp:{accountReference}:locations";
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<PickupLocation>? cached) && cached is not null)
        {
            return cached;
        }

        var locations = await FetchLocationsAsync(accountReference, account, cancellationToken)
            .ConfigureAwait(false);
        _cache.Set(cacheKey, locations, account.CacheDuration);
        return locations;
    }

    private async Task<IReadOnlyList<PickupLocation>> FetchLocationsAsync(
        string accountReference,
        DroppAccountOptions account,
        CancellationToken cancellationToken)
    {
        var baseUri = ValidateBaseUri(account.ApiUrl, accountReference);
        var requestUri = new Uri(baseUri, $"dropp/locations?store={Uri.EscapeDataString(account.StoreId)}");
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", account.ApiKey);
        using var response = await _httpClientFactory.CreateClient(nameof(DroppCarrier))
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Dropp location lookup failed for account {AccountReference} with status {StatusCode}",
                accountReference,
                (int)response.StatusCode);
            throw new ShippingProviderException(
                DroppShippingDefaults.CarrierAlias,
                $"Dropp location lookup failed with HTTP status {(int)response.StatusCode}.");
        }

        DroppLocationsResponse? payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<DroppLocationsResponse>(
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ShippingProviderException(
                DroppShippingDefaults.CarrierAlias,
                "Dropp returned an invalid response.",
                exception);
        }

        if (payload?.Locations is null)
        {
            throw new ShippingProviderException(
                DroppShippingDefaults.CarrierAlias,
                "Dropp returned a response without a locations collection.");
        }

        return payload.Locations
            .Where(x => !string.IsNullOrWhiteSpace(x.Id) && !string.IsNullOrWhiteSpace(x.Name))
            .Select(x => new PickupLocation(
                x.Id,
                x.Name,
                x.Address ?? string.Empty,
                x.AddressObject?.Zip?.ToString() ?? string.Empty,
                x.AddressObject?.Town ?? string.Empty,
                ExternalId: x.ExternalLocationId))
            .ToArray();
    }

    private DroppAccountOptions GetAccount(string accountReference)
    {
        if (string.IsNullOrWhiteSpace(accountReference) ||
            !_options.Accounts.TryGetValue(accountReference, out var account))
        {
            throw new ShippingConfigurationException(
                $"Dropp account '{accountReference}' is not configured.");
        }

        if (string.IsNullOrWhiteSpace(account.ApiKey) || string.IsNullOrWhiteSpace(account.StoreId))
        {
            throw new ShippingConfigurationException(
                $"Dropp account '{accountReference}' has incomplete credentials.");
        }

        _ = ValidateBaseUri(account.ApiUrl, accountReference);
        return account;
    }

    private static Uri ValidateBaseUri(string apiUrl, string accountReference)
    {
        if (!Uri.TryCreate(apiUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ShippingConfigurationException(
                $"Dropp account '{accountReference}' must use an absolute HTTPS API URL.");
        }

        return uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal) ? uri : new Uri(uri.AbsoluteUri + '/');
    }

    private sealed class DroppLocationsResponse
    {
        [JsonPropertyName("locations")]
        public DroppLocation[]? Locations { get; init; }
    }

    private sealed class DroppLocation
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("address")]
        public string? Address { get; init; }

        [JsonPropertyName("externalLocationId")]
        public string? ExternalLocationId { get; init; }

        [JsonPropertyName("addressObject")]
        public DroppAddress? AddressObject { get; init; }
    }

    private sealed class DroppAddress
    {
        [JsonPropertyName("zip")]
        public long? Zip { get; init; }

        [JsonPropertyName("town")]
        public string? Town { get; init; }
    }
}
