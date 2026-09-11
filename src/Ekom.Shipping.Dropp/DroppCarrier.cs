using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ekom.Shipping.Dropp;

internal sealed class DroppCarrier : IDroppShippingService
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
        ShippingCarrierCapabilities.Services |
        ShippingCarrierCapabilities.PickupLocations |
        ShippingCarrierCapabilities.ShipmentBooking |
        ShippingCarrierCapabilities.Labels |
        ShippingCarrierCapabilities.Tracking;

    public ShippingFulfillmentMode GetFulfillmentMode(string accountReference) =>
        GetAccount(accountReference).FulfillmentMode;

    public Task<IReadOnlyList<ShippingService>> GetServicesAsync(
        string accountReference,
        ShippingLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        _ = GetAccount(accountReference);
        IReadOnlyList<ShippingService> services =
        [
            new ShippingService(DroppShippingDefaults.PickupServiceId, "Dropp pickup", true),
            new ShippingService(DroppShippingDefaults.HomeDeliveryServiceId, "Dropp home delivery", false),
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

    public async Task<string?> ReserveBookingReferenceAsync(
        string accountReference,
        CancellationToken cancellationToken = default)
    {
        var account = GetAccount(accountReference);
        using var request = CreateRequest(account, HttpMethod.Get, "orders/barcode/");
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, accountReference, "barcode allocation");
        var payload = await ReadJsonAsync<DroppBarcodeResponse>(response, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(payload?.Barcode))
        {
            throw new ShippingProviderException(
                DroppShippingDefaults.CarrierAlias,
                "Dropp returned no booking barcode.");
        }

        return payload.Barcode;
    }

    public async Task<ShipmentBookingResult> CreateShipmentAsync(
        string accountReference,
        ShipmentBookingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var account = GetAccount(accountReference);
        if (string.IsNullOrWhiteSpace(request.BookingReference))
        {
            throw new ArgumentException("A reserved Dropp barcode is required.", nameof(request));
        }

        var locationId = ResolveLocationId(account, request);
        if (string.Equals(request.ServiceId, DroppShippingDefaults.PickupServiceId, StringComparison.Ordinal))
        {
            var availableLocations = await GetPickupLocationsAsync(
                accountReference,
                request.ServiceId,
                new ShippingLookupRequest(string.Empty, request.Recipient.CountryCode, request.Recipient.PostalCode),
                cancellationToken).ConfigureAwait(false);
            if (!availableLocations.Any(x =>
                    string.Equals(x.Id, locationId.ToString(), StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidShippingSelectionException(
                    $"Dropp pickup location '{locationId}' is no longer available.");
            }
        }

        var payload = new DroppOrderRequest
        {
            LocationId = locationId,
            Barcode = request.BookingReference,
            Value = request.Value,
            Products = request.Items.Select(x => new DroppProductRequest
            {
                Name = x.Name,
                Barcode = x.Sku,
                Quantity = x.Quantity,
            }).ToArray(),
            Customer = new DroppCustomerRequest
            {
                Name = request.Recipient.Name,
                Email = request.Recipient.Email,
                NationalId = request.Recipient.NationalId,
                Address = request.Recipient.Address,
                Phone = request.Recipient.Phone,
                PostalCode = ParsePostalCode(request.Recipient.PostalCode),
                City = request.Recipient.City,
            },
        };

        using var httpRequest = CreateRequest(account, HttpMethod.Post, "orders/");
        httpRequest.Content = JsonContent.Create(payload);
        HttpResponseMessage response;
        try
        {
            response = await SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ShipmentOutcomeUnknownException(
                "Dropp shipment submission ended without a definitive response.",
                exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var statusCode = (int)response.StatusCode;
                if (statusCode is >= 400 and < 500 && statusCode is not 408 and not 409 and not 429)
                {
                    EnsureSuccess(response, accountReference, "shipment creation");
                }

                throw new ShipmentOutcomeUnknownException(
                    $"Dropp shipment submission returned HTTP status {statusCode}; its outcome is uncertain.",
                    new HttpRequestException($"Dropp returned HTTP status {statusCode}."));
            }

            DroppOrderResponse? created;
            try
            {
                created = await response.Content.ReadFromJsonAsync<DroppOrderResponse>(
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new ShipmentOutcomeUnknownException(
                    "Dropp returned an invalid response after accepting the shipment request.",
                    exception);
            }

            if (created is null || created.Id == Guid.Empty)
            {
                throw new ShipmentOutcomeUnknownException(
                    "Dropp accepted the shipment request but returned no order ID.",
                    new InvalidOperationException("The Dropp order ID was empty."));
            }

            return new ShipmentBookingResult(
                created.Id.ToString(),
                request.BookingReference,
                request.BookingReference);
        }
    }

    public async Task<ShippingLabel> GetLabelAsync(
        string accountReference,
        string shipmentId,
        CancellationToken cancellationToken = default)
    {
        var account = GetAccount(accountReference);
        if (!Guid.TryParse(shipmentId, out var orderId) || orderId == Guid.Empty)
        {
            throw new ArgumentException("A valid Dropp order ID is required.", nameof(shipmentId));
        }

        using var request = CreateRequest(account, HttpMethod.Get, $"orders/pdf/{orderId}");
        request.Headers.Accept.Clear();
        request.Headers.Accept.ParseAdd("application/pdf");
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, accountReference, "label retrieval");
        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (content.Length == 0)
        {
            throw new ShippingProviderException(DroppShippingDefaults.CarrierAlias, "Dropp returned an empty label.");
        }

        return new ShippingLabel(content, "application/pdf", $"dropp-label-{orderId}.pdf");
    }

    public async Task<ShippingTrackingResult> GetTrackingAsync(
        string accountReference,
        string trackingNumber,
        string language,
        CancellationToken cancellationToken = default)
    {
        var account = GetAccount(accountReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(trackingNumber);
        if (language is not ("is" or "en"))
        {
            throw new ArgumentOutOfRangeException(nameof(language), "Tracking language must be 'is' or 'en'.");
        }

        var path = $"dropp/tracking/json/{Uri.EscapeDataString(trackingNumber)}/{language}";
        using var request = CreateRequest(account, HttpMethod.Get, path);
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, accountReference, "tracking lookup");
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var _ = JsonDocument.Parse(content);
        }
        catch (JsonException exception)
        {
            throw new ShippingProviderException(
                DroppShippingDefaults.CarrierAlias,
                "Dropp returned invalid tracking data.",
                exception);
        }

        return new ShippingTrackingResult(content);
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

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        await _httpClientFactory.CreateClient(nameof(DroppCarrier))
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

    private static HttpRequestMessage CreateRequest(
        DroppAccountOptions account,
        HttpMethod method,
        string path)
    {
        var request = new HttpRequestMessage(method, new Uri(ValidateBaseUri(account.ApiUrl, "configured"), path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", account.ApiKey);
        request.Headers.Accept.ParseAdd("application/json");
        return request;
    }

    private void EnsureSuccess(HttpResponseMessage response, string accountReference, string operation)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        _logger.LogWarning(
            "Dropp {Operation} failed for account {AccountReference} with status {StatusCode}",
            operation,
            accountReference,
            (int)response.StatusCode);
        throw new ShippingProviderException(
            DroppShippingDefaults.CarrierAlias,
            $"Dropp {operation} failed with HTTP status {(int)response.StatusCode}.");
    }

    private static async Task<T?> ReadJsonAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ShippingProviderException(
                DroppShippingDefaults.CarrierAlias,
                "Dropp returned an invalid response.",
                exception);
        }
    }

    private static Guid ResolveLocationId(DroppAccountOptions account, ShipmentBookingRequest request)
    {
        if (string.Equals(
                request.ServiceId,
                DroppShippingDefaults.HomeDeliveryServiceId,
                StringComparison.Ordinal))
        {
            if (account.HomeDeliveryLocationId == Guid.Empty)
            {
                throw new ShippingConfigurationException("Dropp home delivery location ID is not configured.");
            }

            return account.HomeDeliveryLocationId;
        }

        if (string.Equals(request.ServiceId, DroppShippingDefaults.PickupServiceId, StringComparison.Ordinal) &&
            Guid.TryParse(request.PickupLocationId, out var pickupLocationId) &&
            pickupLocationId != Guid.Empty)
        {
            return pickupLocationId;
        }

        throw new InvalidShippingSelectionException("A valid Dropp pickup location is required.");
    }

    private static short ParsePostalCode(string value)
    {
        if (!short.TryParse(value, out var postalCode) || postalCode <= 0)
        {
            throw new ArgumentException("Dropp requires a positive numeric postal code.", nameof(value));
        }

        return postalCode;
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

    private sealed class DroppBarcodeResponse
    {
        [JsonPropertyName("barcode")]
        public string? Barcode { get; init; }
    }

    private sealed class DroppOrderRequest
    {
        [JsonPropertyName("locationId")]
        public Guid LocationId { get; init; }

        [JsonPropertyName("barcode")]
        public string Barcode { get; init; } = string.Empty;

        [JsonPropertyName("value")]
        public decimal Value { get; init; }

        [JsonPropertyName("products")]
        public IReadOnlyList<DroppProductRequest> Products { get; init; } = [];

        [JsonPropertyName("customer")]
        public DroppCustomerRequest Customer { get; init; } = new();
    }

    private sealed class DroppOrderResponse
    {
        [JsonPropertyName("id")]
        public Guid Id { get; init; }
    }

    private sealed class DroppProductRequest
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("barcode")]
        public string Barcode { get; init; } = string.Empty;

        [JsonPropertyName("quantity")]
        public int Quantity { get; init; }
    }

    private sealed class DroppCustomerRequest
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("emailAddress")]
        public string Email { get; init; } = string.Empty;

        [JsonPropertyName("socialSecurityNumber")]
        public string? NationalId { get; init; }

        [JsonPropertyName("address")]
        public string Address { get; init; } = string.Empty;

        [JsonPropertyName("phoneNumber")]
        public string Phone { get; init; } = string.Empty;

        [JsonPropertyName("zipcode")]
        public short PostalCode { get; init; }

        [JsonPropertyName("town")]
        public string City { get; init; } = string.Empty;
    }
}
