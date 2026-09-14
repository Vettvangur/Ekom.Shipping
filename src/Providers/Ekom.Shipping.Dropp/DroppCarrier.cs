using System.Collections.Concurrent;
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
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<DroppCarrier> _logger;
    private readonly DroppOptions _options;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _cacheLocks = new(StringComparer.Ordinal);

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
        ShippingCarrierCapabilities.Tracking |
        ShippingCarrierCapabilities.ShipmentLookup |
        ShippingCarrierCapabilities.ShipmentDeletion;

    public ShippingFulfillmentMode GetFulfillmentMode(string accountReference) =>
        GetAccount(accountReference).FulfillmentMode;

    public async Task<IReadOnlyList<ShippingService>> GetServicesAsync(
        string accountReference,
        ShippingLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        _ = GetAccount(accountReference);
        List<ShippingService> services =
        [
            new ShippingService(DroppShippingDefaults.PickupServiceId, "Dropp pickup", true),
        ];

        var canOfferHomeDelivery = string.IsNullOrWhiteSpace(request.PostalCode) ||
            (short.TryParse(request.PostalCode, out var postalCode) &&
             (await GetDeliveryPostalCodesAsync(accountReference, cancellationToken).ConfigureAwait(false))
                 .Codes.Any(x => x.Code == postalCode));
        if (canOfferHomeDelivery && string.Equals(request.CountryCode, "IS", StringComparison.OrdinalIgnoreCase))
        {
            services.Add(new ShippingService(
                DroppShippingDefaults.HomeDeliveryServiceId,
                "Dropp home delivery",
                false));
        }

        return services;
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
        return await GetOrCreateCachedAsync(
            cacheKey,
            account.CacheDuration,
            ct => FetchLocationsAsync(accountReference, account, ct),
            cancellationToken).ConfigureAwait(false);
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

    public async Task<DroppDeliveryPostalCodes> GetDeliveryPostalCodesAsync(
        string accountReference,
        CancellationToken cancellationToken = default)
    {
        var account = GetAccount(accountReference);
        var cacheKey = $"ekom-shipping:dropp:{accountReference}:delivery-postal-codes";
        return await GetOrCreateCachedAsync(
            cacheKey,
            account.CacheDuration,
            async ct =>
            {
                using var request = CreateRequest(account, HttpMethod.Get, "dropp/location/deliveryzips");
                using var response = await SendAsync(request, ct).ConfigureAwait(false);
                EnsureSuccess(response, accountReference, "delivery postal-code lookup");
                return await ReadJsonAsync<DroppDeliveryPostalCodes>(response, ct)
                    .ConfigureAwait(false)
                    ?? throw new ShippingProviderException(
                        DroppShippingDefaults.CarrierAlias,
                        "Dropp returned no delivery postal codes.");
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<DroppOrder> CreateOrderAsync(
        string accountReference,
        DroppCreateOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCreateOrderRequest(request);
        var account = GetAccount(accountReference);
        using var httpRequest = CreateRequest(account, HttpMethod.Post, "orders/");
        httpRequest.Content = JsonContent.Create(request, options: JsonOptions);
        using var response = await SendMutationAsync(
            httpRequest,
            accountReference,
            "shipment creation",
            cancellationToken).ConfigureAwait(false);

        try
        {
            var created = await response.Content.ReadFromJsonAsync<DroppOrder>(
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            if (created is null || created.Id == Guid.Empty)
            {
                throw new JsonException("The Dropp order ID was empty.");
            }

            return created;
        }
        catch (Exception exception) when (exception is not ShipmentOutcomeUnknownException)
        {
            throw new ShipmentOutcomeUnknownException(
                "Dropp returned an invalid response after accepting the shipment request.",
                exception);
        }
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

        if (string.Equals(request.ServiceId, DroppShippingDefaults.HomeDeliveryServiceId, StringComparison.Ordinal))
        {
            if (!string.Equals(request.Recipient.CountryCode, "IS", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidShippingSelectionException("Dropp home delivery is only available in Iceland.");
            }

            var postalCode = ParsePostalCode(request.Recipient.PostalCode);
            var deliveryPostalCodes = await GetDeliveryPostalCodesAsync(accountReference, cancellationToken)
                .ConfigureAwait(false);
            if (!deliveryPostalCodes.Codes.Any(x => x.Code == postalCode))
            {
                throw new InvalidShippingSelectionException(
                    $"Dropp home delivery is not available for postal code '{postalCode}'.");
            }
        }

        var created = await CreateOrderAsync(
            accountReference,
            new DroppCreateOrderRequest(
                locationId,
                request.Value,
                request.Items.Select(x => new DroppProduct(x.Name, x.Sku, x.Quantity)).ToArray(),
                new DroppCustomer(
                    request.Recipient.Name,
                    request.Recipient.Address,
                    request.Recipient.Phone,
                    ParsePostalCode(request.Recipient.PostalCode),
                    request.Recipient.City,
                    request.Recipient.Email,
                    request.Recipient.NationalId),
                request.BookingReference),
            cancellationToken).ConfigureAwait(false);
        var barcode = string.IsNullOrWhiteSpace(created.Barcode)
            ? request.BookingReference
            : created.Barcode;
        return new ShipmentBookingResult(created.Id.ToString(), barcode, barcode);
    }

    public async Task<DroppOrder> GetOrderAsync(
        string accountReference,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("A valid Dropp order ID is required.", nameof(orderId));
        }

        var account = GetAccount(accountReference);
        using var request = CreateRequest(account, HttpMethod.Get, $"orders/{orderId}");
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, accountReference, "order lookup");
        return await ReadJsonAsync<DroppOrder>(response, cancellationToken).ConfigureAwait(false)
            ?? throw new ShippingProviderException(
                DroppShippingDefaults.CarrierAlias,
                "Dropp returned no order.");
    }

    public async Task UpdateOrderAsync(
        string accountReference,
        Guid orderId,
        DroppUpdateOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("A valid Dropp order ID is required.", nameof(orderId));
        }

        ArgumentNullException.ThrowIfNull(request);
        if (request.Barcode is null && request.LocationId is null && request.Customer is null && request.Products is null)
        {
            throw new ArgumentException("At least one Dropp order field must be updated.", nameof(request));
        }

        if (request.Customer is not null)
        {
            ValidateCustomer(request.Customer);
        }

        if (request.Products is not null)
        {
            ValidateProducts(request.Products);
        }

        var account = GetAccount(accountReference);
        using var httpRequest = CreateRequest(account, HttpMethod.Patch, $"orders/{orderId}");
        httpRequest.Content = JsonContent.Create(request, options: JsonOptions);
        using var response = await SendMutationAsync(
            httpRequest,
            accountReference,
            "order update",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteOrderAsync(
        string accountReference,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("A valid Dropp order ID is required.", nameof(orderId));
        }

        var existing = await GetOrderAsync(accountReference, orderId, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(existing.Status, "initial", StringComparison.OrdinalIgnoreCase))
        {
            throw new ShippingProviderException(
                DroppShippingDefaults.CarrierAlias,
                $"Dropp order '{orderId}' can no longer be deleted because its status is '{existing.Status}'.");
        }

        var account = GetAccount(accountReference);
        using var request = CreateRequest(account, HttpMethod.Delete, $"orders/{orderId}");
        using var response = await SendMutationAsync(
            request,
            accountReference,
            "order deletion",
            cancellationToken).ConfigureAwait(false);
    }

    async Task<ShippingShipmentDocument> IShippingShipmentLookupCarrier.GetShipmentAsync(
        string accountReference,
        string shipmentId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(shipmentId, out var orderId) || orderId == Guid.Empty)
        {
            throw new ArgumentException("A valid Dropp order ID is required.", nameof(shipmentId));
        }

        var account = GetAccount(accountReference);
        using var request = CreateRequest(account, HttpMethod.Get, $"orders/{orderId}");
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, accountReference, "order lookup");
        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        string? status;
        try
        {
            using var json = JsonDocument.Parse(content);
            status = json.RootElement.TryGetProperty("status", out var statusProperty)
                ? statusProperty.GetString()
                : null;
        }
        catch (JsonException exception)
        {
            throw new ShippingProviderException(
                DroppShippingDefaults.CarrierAlias,
                "Dropp returned invalid order data.",
                exception);
        }

        return new ShippingShipmentDocument(
            content,
            "application/json",
            $"dropp-order-{orderId}.json",
            status);
    }

    async Task IShippingShipmentDeletionCarrier.DeleteShipmentAsync(
        string accountReference,
        string shipmentId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(shipmentId, out var orderId) || orderId == Guid.Empty)
        {
            throw new ArgumentException("A valid Dropp order ID is required.", nameof(shipmentId));
        }

        await DeleteOrderAsync(accountReference, orderId, cancellationToken).ConfigureAwait(false);
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

    public async Task<ShippingLabel> GetReturnLabelAsync(
        string accountReference,
        string barcode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(barcode);
        return await GetPdfAsync(
            accountReference,
            $"orders/returnpdf/{Uri.EscapeDataString(barcode)}",
            $"dropp-return-label-{SanitizeFileName(barcode)}.pdf",
            "return-label retrieval",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ShippingLabel> CreateExtraPackageLabelAsync(
        string accountReference,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("A valid Dropp order ID is required.", nameof(orderId));
        }

        var account = GetAccount(accountReference);
        using var request = CreateRequest(account, HttpMethod.Get, $"orders/extrapdf/{orderId}");
        request.Headers.Accept.Clear();
        request.Headers.Accept.ParseAdd("application/pdf");
        using var response = await SendMutationAsync(
            request,
            accountReference,
            "extra-package creation",
            cancellationToken).ConfigureAwait(false);
        byte[] content;
        try
        {
            content = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            throw new ShipmentOutcomeUnknownException(
                "Dropp accepted the extra-package request but its label could not be read.",
                exception);
        }
        if (content.Length == 0)
        {
            throw new ShipmentOutcomeUnknownException(
                "Dropp returned an empty extra-package label after accepting the request.",
                new InvalidOperationException("The label was empty."));
        }

        return new ShippingLabel(content, "application/pdf", $"dropp-extra-label-{orderId}.pdf");
    }

    public async Task DeleteExtraOrderAsync(
        string accountReference,
        Guid orderId,
        string barcode,
        CancellationToken cancellationToken = default)
    {
        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("A valid Dropp order ID is required.", nameof(orderId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(barcode);
        var account = GetAccount(accountReference);
        var path = $"orders/deleteextraorder/{orderId}/{Uri.EscapeDataString(barcode)}/";
        using var request = CreateRequest(account, HttpMethod.Get, path);
        using var response = await SendMutationAsync(
            request,
            accountReference,
            "extra-order deletion",
            cancellationToken).ConfigureAwait(false);
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

    private async Task<T> GetOrCreateCachedAsync<T>(
        string cacheKey,
        TimeSpan cacheDuration,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken)
        where T : class
    {
        if (_cache.TryGetValue(cacheKey, out T? cached) && cached is not null)
        {
            return cached;
        }

        var cacheLock = _cacheLocks.GetOrAdd(cacheKey, static _ => new SemaphoreSlim(1, 1));
        await cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cache.TryGetValue(cacheKey, out cached) && cached is not null)
            {
                return cached;
            }

            var result = await factory(cancellationToken).ConfigureAwait(false);
            _cache.Set(cacheKey, result, cacheDuration);
            return result;
        }
        finally
        {
            cacheLock.Release();
        }
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

    private async Task<ShippingLabel> GetPdfAsync(
        string accountReference,
        string path,
        string fileName,
        string operation,
        CancellationToken cancellationToken)
    {
        var account = GetAccount(accountReference);
        using var request = CreateRequest(account, HttpMethod.Get, path);
        request.Headers.Accept.Clear();
        request.Headers.Accept.ParseAdd("application/pdf");
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, accountReference, operation);
        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (content.Length == 0)
        {
            throw new ShippingProviderException(DroppShippingDefaults.CarrierAlias, "Dropp returned an empty label.");
        }

        return new ShippingLabel(content, "application/pdf", fileName);
    }

    private async Task<HttpResponseMessage> SendMutationAsync(
        HttpRequestMessage request,
        string accountReference,
        string operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        HttpResponseMessage response;
        try
        {
            response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            throw new ShipmentOutcomeUnknownException(
                $"Dropp {operation} ended without a definitive response.",
                exception);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        var statusCode = (int)response.StatusCode;
        response.Dispose();
        if (statusCode is >= 400 and < 500 && statusCode is not 408 and not 409 and not 429)
        {
            _logger.LogWarning(
                "Dropp {Operation} failed for account {AccountReference} with status {StatusCode}",
                operation,
                accountReference,
                statusCode);
            throw new ShippingProviderException(
                DroppShippingDefaults.CarrierAlias,
                $"Dropp {operation} failed with HTTP status {statusCode}.");
        }

        throw new ShipmentOutcomeUnknownException(
            $"Dropp {operation} returned HTTP status {statusCode}; its outcome is uncertain.",
            new HttpRequestException($"Dropp returned HTTP status {statusCode}."));
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

    private static void ValidateCreateOrderRequest(DroppCreateOrderRequest request)
    {
        if (request.LocationId == Guid.Empty)
        {
            throw new ArgumentException("A valid Dropp location ID is required.", nameof(request));
        }

        if (request.Value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Dropp order value cannot be negative.");
        }

        ValidateProducts(request.Products);
        ValidateCustomer(request.Customer);
    }

    private static void ValidateProducts(IReadOnlyList<DroppProduct> products)
    {
        ArgumentNullException.ThrowIfNull(products);
        if (products.Count == 0 || products.Any(x =>
                string.IsNullOrWhiteSpace(x.Name) ||
                string.IsNullOrWhiteSpace(x.Barcode) ||
                x.Quantity <= 0))
        {
            throw new ArgumentException(
                "Dropp requires at least one product with a name, barcode, and positive quantity.",
                nameof(products));
        }
    }

    private static void ValidateCustomer(DroppCustomer customer)
    {
        ArgumentNullException.ThrowIfNull(customer);
        if (string.IsNullOrWhiteSpace(customer.Name) ||
            string.IsNullOrWhiteSpace(customer.Address) ||
            string.IsNullOrWhiteSpace(customer.PhoneNumber) ||
            customer.Zipcode <= 0 ||
            string.IsNullOrWhiteSpace(customer.Town))
        {
            throw new ArgumentException(
                "Dropp requires customer name, address, phone number, postal code, and town.",
                nameof(customer));
        }
    }

    private static string SanitizeFileName(string value) =>
        string.Concat(value.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

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

}
