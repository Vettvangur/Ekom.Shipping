using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ekom.Shipping.IcelandicPost;

internal sealed class IcelandicPostCarrier : IIcelandicPostShippingService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<IcelandicPostCarrier> _logger;
    private readonly IcelandicPostOptions _options;

    public IcelandicPostCarrier(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        IOptions<IcelandicPostOptions> options,
        ILogger<IcelandicPostCarrier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public string Alias => IcelandicPostShippingDefaults.CarrierAlias;

    public ShippingCarrierCapabilities Capabilities =>
        ShippingCarrierCapabilities.Services | ShippingCarrierCapabilities.PickupLocations;

    public async Task<IReadOnlyList<ShippingService>> GetServicesAsync(
        string accountReference,
        ShippingLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        var account = GetAccount(accountReference);
        if (string.IsNullOrWhiteSpace(request.CountryCode) || string.IsNullOrWhiteSpace(request.PostalCode))
        {
            return Array.Empty<ShippingService>();
        }

        var uri = BuildUri(
            account,
            $"wscm/v1/deliveryservicesandprices?countryCode={Uri.EscapeDataString(request.CountryCode)}&postCode={Uri.EscapeDataString(request.PostalCode)}");
        var payload = await GetAsync<DeliveryServicesResponse>(
            accountReference,
            account,
            uri,
            cancellationToken).ConfigureAwait(false);

        if (payload?.DeliveryServicesAndPrices is null)
        {
            throw new ShippingProviderException(
                IcelandicPostShippingDefaults.CarrierAlias,
                "Íslandspóstur returned a response without a services collection.");
        }

        return payload.DeliveryServicesAndPrices
            .Where(x => !string.IsNullOrWhiteSpace(x.DeliveryServiceId))
            .Select(x => new ShippingService(
                x.DeliveryServiceId,
                string.IsNullOrWhiteSpace(x.NameLong) ? x.DeliveryServiceId : x.NameLong,
                string.Equals(
                    x.DeliveryServiceId,
                    IcelandicPostShippingDefaults.PostboxServiceId,
                    StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    public async Task<IReadOnlyList<PickupLocation>> GetPickupLocationsAsync(
        string accountReference,
        string serviceId,
        ShippingLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(
                serviceId,
                IcelandicPostShippingDefaults.PostboxServiceId,
                StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<PickupLocation>();
        }

        var account = GetAccount(accountReference);
        var cacheKey = $"ekom-shipping:icelandic-post:{accountReference}:postboxes";
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<PickupLocation>? cached) && cached is not null)
        {
            return cached;
        }

        var payload = await GetAsync<PostboxesResponse>(
            accountReference,
            account,
            BuildUri(account, "wscm/v1/postboxes"),
            cancellationToken).ConfigureAwait(false);
        if (payload?.Postboxes is null)
        {
            throw new ShippingProviderException(
                IcelandicPostShippingDefaults.CarrierAlias,
                "Íslandspóstur returned a response without a postboxes collection.");
        }

        var locations = payload.Postboxes
            .Where(x => !string.IsNullOrWhiteSpace(x.PostboxId) && !string.IsNullOrWhiteSpace(x.Name))
            .Select(x => new PickupLocation(
                x.PostboxId,
                x.Name,
                x.Address ?? string.Empty,
                x.Postcode ?? string.Empty,
                x.Town ?? string.Empty,
                x.Latitude,
                x.Longitude))
            .ToArray();

        _cache.Set(cacheKey, locations, account.CacheDuration);
        return locations;
    }

    private async Task<T?> GetAsync<T>(
        string accountReference,
        IcelandicPostAccountOptions account,
        Uri uri,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Add("x-api-key", account.ApiKey);
        using var response = await _httpClientFactory.CreateClient(nameof(IcelandicPostCarrier))
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Íslandspóstur lookup failed for account {AccountReference} with status {StatusCode}",
                accountReference,
                (int)response.StatusCode);
            throw new ShippingProviderException(
                IcelandicPostShippingDefaults.CarrierAlias,
                $"Íslandspóstur lookup failed with HTTP status {(int)response.StatusCode}.");
        }

        try
        {
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ShippingProviderException(
                IcelandicPostShippingDefaults.CarrierAlias,
                "Íslandspóstur returned an invalid response.",
                exception);
        }
    }

    private IcelandicPostAccountOptions GetAccount(string accountReference)
    {
        if (string.IsNullOrWhiteSpace(accountReference) ||
            !_options.Accounts.TryGetValue(accountReference, out var account))
        {
            throw new ShippingConfigurationException(
                $"Íslandspóstur account '{accountReference}' is not configured.");
        }

        if (string.IsNullOrWhiteSpace(account.ApiKey))
        {
            throw new ShippingConfigurationException(
                $"Íslandspóstur account '{accountReference}' has incomplete credentials.");
        }

        _ = BuildUri(account, string.Empty);
        return account;
    }

    private static Uri BuildUri(IcelandicPostAccountOptions account, string path)
    {
        if (!Uri.TryCreate(account.ApiUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ShippingConfigurationException(
                "Íslandspóstur accounts must use an absolute HTTPS API URL.");
        }

        if (!baseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal))
        {
            baseUri = new Uri(baseUri.AbsoluteUri + '/');
        }

        return new Uri(baseUri, path);
    }

    private sealed class PostboxesResponse
    {
        [JsonPropertyName("postboxes")]
        public PostboxResponse[]? Postboxes { get; init; }
    }

    private sealed class PostboxResponse
    {
        public string PostboxId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string? Address { get; init; }
        public string? Postcode { get; init; }
        public string? Town { get; init; }
        public double? Latitude { get; init; }
        public double? Longitude { get; init; }
    }

    private sealed class DeliveryServicesResponse
    {
        public DeliveryServiceResponse[]? DeliveryServicesAndPrices { get; init; }
    }

    private sealed class DeliveryServiceResponse
    {
        public string DeliveryServiceId { get; init; } = string.Empty;
        public string NameLong { get; init; } = string.Empty;
    }
}
