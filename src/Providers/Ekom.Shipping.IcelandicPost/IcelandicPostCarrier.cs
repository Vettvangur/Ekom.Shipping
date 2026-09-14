using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ekom.Shipping.IcelandicPost;

internal sealed class IcelandicPostCarrier : IIcelandicPostShippingService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<IcelandicPostCarrier> _logger;
    private readonly IcelandicPostOptions _options;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _cacheLocks = new(StringComparer.Ordinal);

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
        var services = await GetDeliveryServicesAsync(
            accountReference,
            new IcelandicPostServiceRequest(
                request.CountryCode,
                string.Equals(request.CountryCode, "IS", StringComparison.OrdinalIgnoreCase)
                    ? request.PostalCode
                    : null),
            cancellationToken).ConfigureAwait(false);

        return services
            .Where(x => !string.IsNullOrWhiteSpace(x.DeliveryServiceId))
            .Select(x => new ShippingService(
                x.DeliveryServiceId,
                string.IsNullOrWhiteSpace(x.NameLong) ? x.DeliveryServiceId : x.NameLong,
                RequiresPickupSelection(x.DeliveryServiceId)))
            .ToArray();
    }

    public async Task<IReadOnlyList<IcelandicPostDeliveryService>> GetDeliveryServicesAsync(
        string accountReference,
        IcelandicPostServiceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (new[] { request.Height, request.Length, request.Width }.Count(x => x.HasValue) is > 0 and < 3)
        {
            throw new ArgumentException("Height, length, and width must be supplied together.", nameof(request));
        }

        var account = GetAccount(accountReference);
        var query = new List<KeyValuePair<string, string?>>
        {
            new("countryCode", request.CountryCode),
            new("postCode", request.Postcode),
            new("weight", request.Weight?.ToString(CultureInfo.InvariantCulture)),
            new("height", request.Height?.ToString(CultureInfo.InvariantCulture)),
            new("length", request.Length?.ToString(CultureInfo.InvariantCulture)),
            new("width", request.Width?.ToString(CultureInfo.InvariantCulture)),
            new("cartAmount", request.CartAmount?.ToString(CultureInfo.InvariantCulture)),
        };
        var payload = await GetJsonAsync<DeliveryServicesResponse>(
            accountReference,
            account,
            BuildUri(account, "v1/deliveryservicesandprices", query),
            cancellationToken).ConfigureAwait(false);
        return payload?.DeliveryServicesAndPrices
            ?? throw InvalidResponse("delivery services collection");
    }

    public async Task<IReadOnlyList<PickupLocation>> GetPickupLocationsAsync(
        string accountReference,
        string serviceId,
        ShippingLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        var postcode = int.TryParse(request.PostalCode, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedPostcode)
            && parsedPostcode is >= 100 and <= 999
                ? parsedPostcode
                : (int?)null;
        IReadOnlyList<IcelandicPostPickupLocation> locations = serviceId.ToUpperInvariant() switch
        {
            "DPO" or "DNO" => await GetPostboxesAsync(
                accountReference,
                postcode,
                cancellationToken: cancellationToken).ConfigureAwait(false),
            "DPT" => await GetParcelPointsAsync(
                accountReference,
                postcode,
                cancellationToken: cancellationToken).ConfigureAwait(false),
            "DPP" => await GetPostOfficesAsync(accountReference, cancellationToken).ConfigureAwait(false),
            _ => [],
        };

        return locations.Select(x => new PickupLocation(
            x.Id,
            x.Name,
            x.Address,
            x.Postcode,
            x.Town,
            x.Latitude,
            x.Longitude)).ToArray();
    }

    public Task<IReadOnlyList<IcelandicPostPickupLocation>> GetPostboxesAsync(
        string accountReference,
        int? postcode = null,
        int? maxResults = null,
        CancellationToken cancellationToken = default) =>
        GetLocationsAsync(accountReference, "postboxes", "postboxes", postcode, maxResults, cancellationToken);

    public Task<IReadOnlyList<IcelandicPostPickupLocation>> GetParcelPointsAsync(
        string accountReference,
        int? postcode = null,
        int? maxResults = null,
        CancellationToken cancellationToken = default) =>
        GetLocationsAsync(accountReference, "parcelpoints", "parcelPoints", postcode, maxResults, cancellationToken);

    public async Task<IReadOnlyList<IcelandicPostPickupLocation>> GetPostOfficesAsync(
        string accountReference,
        CancellationToken cancellationToken = default)
    {
        var account = GetAccount(accountReference);
        var cacheKey = $"ekom-shipping:icelandic-post:{accountReference}:postoffices";
        return await GetOrCreateCachedAsync<IReadOnlyList<IcelandicPostPickupLocation>>(
            cacheKey,
            account.CacheDuration,
            async ct =>
            {
                var payload = await GetJsonAsync<PostOfficesResponse>(
                    accountReference,
                    account,
                    BuildUri(account, "v1/postoffices"),
                    ct).ConfigureAwait(false);
                return payload?.PostOffices?.Select(x => x.ToLocation()).ToArray()
                    ?? throw InvalidResponse("post offices collection");
            },
            cancellationToken).ConfigureAwait(false);
    }

    public Task<string?> ReserveBookingReferenceAsync(
        string accountReference,
        CancellationToken cancellationToken = default)
    {
        _ = GetAccount(accountReference);
        return Task.FromResult<string?>(null);
    }

    public async Task<ShipmentBookingResult> CreateShipmentAsync(
        string accountReference,
        ShipmentBookingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.Recipient.CountryCode, "IS", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidShippingSelectionException(
                "Built-in Íslandspóstur fulfillment supports domestic shipments only. Use IIcelandicPostShippingService for international shipments.");
        }

        if (RequiresPickupSelection(request.ServiceId))
        {
            throw new InvalidShippingSelectionException(
                $"Íslandspóstur service '{request.ServiceId}' requires a carrier-qualified service ID for fulfillment.");
        }

        if (request.MerchantOrderId.Length > 30)
        {
            throw new ShippingException("Íslandspóstur shipment references cannot exceed 30 characters.");
        }

        var shipment = await CreateShipmentAsync(
            accountReference,
            new IcelandicPostShipmentRequest(
                new IcelandicPostRecipient(
                    request.Recipient.Name,
                    request.Recipient.Address,
                    request.Recipient.PostalCode,
                    "IS",
                    request.Recipient.City,
                    request.Recipient.Email,
                    request.Recipient.Phone,
                    request.Recipient.NationalId),
                new IcelandicPostShipmentOptions
                {
                    DeliveryServiceId = request.ServiceId,
                    Reference = request.MerchantOrderId,
                    NumberOfItems = 1,
                }),
            cancellationToken).ConfigureAwait(false);
        return new ShipmentBookingResult(shipment.ShipmentId, request.MerchantOrderId, shipment.ShipmentId);
    }

    public async Task<IcelandicPostShipment> CreateShipmentAsync(
        string accountReference,
        IcelandicPostShipmentRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateShipmentRequest(request);
        var account = GetAccount(accountReference);
        using var httpRequest = CreateRequest(account, HttpMethod.Post, "v1/shipments/create");
        httpRequest.Content = JsonContent.Create(request, options: JsonOptions);
        using var response = await SendMutationAsync(
            httpRequest,
            accountReference,
            "shipment creation",
            cancellationToken).ConfigureAwait(false);
        try
        {
            var shipment = await response.Content.ReadFromJsonAsync<IcelandicPostShipment>(
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            if (shipment is null || string.IsNullOrWhiteSpace(shipment.ShipmentId))
            {
                throw new JsonException("The shipment ID was empty.");
            }

            return shipment;
        }
        catch (Exception exception) when (exception is not ShipmentOutcomeUnknownException)
        {
            throw new ShipmentOutcomeUnknownException(
                "Íslandspóstur returned an invalid response after accepting the shipment request.",
                exception);
        }
    }

    public async Task<IcelandicPostShipment> GetShipmentAsync(
        string accountReference,
        string shipmentId,
        string language = "IS",
        CancellationToken cancellationToken = default)
    {
        ValidateShipmentId(shipmentId);
        ValidateLanguage(language);
        var account = GetAccount(accountReference);
        var result = await GetJsonAsync<IcelandicPostShipment>(
            accountReference,
            account,
            BuildUri(account, $"v1/shipments/{Uri.EscapeDataString(shipmentId)}", [new("language", language)]),
            cancellationToken).ConfigureAwait(false);
        return result ?? throw InvalidResponse("shipment");
    }

    public async Task DeleteShipmentAsync(
        string accountReference,
        string shipmentId,
        string language = "IS",
        CancellationToken cancellationToken = default)
    {
        ValidateShipmentId(shipmentId);
        ValidateLanguage(language);
        var account = GetAccount(accountReference);
        using var request = CreateRequest(
            account,
            HttpMethod.Delete,
            $"v1/shipments/{Uri.EscapeDataString(shipmentId)}?language={language}");
        using var response = await SendMutationAsync(
            request,
            accountReference,
            "shipment deletion",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IcelandicPostShipmentStatus> GetShipmentStatusAsync(
        string accountReference,
        string shipmentId,
        CancellationToken cancellationToken = default)
    {
        ValidateShipmentId(shipmentId);
        var account = GetAccount(accountReference);
        return await GetJsonAsync<IcelandicPostShipmentStatus>(
            accountReference,
            account,
            BuildUri(account, $"v1/shipments/{Uri.EscapeDataString(shipmentId)}/status"),
            cancellationToken).ConfigureAwait(false)
            ?? throw InvalidResponse("shipment status");
    }

    public async Task<IReadOnlyList<IcelandicPostShipmentReference>> GetShipmentsByReferenceAsync(
        string accountReference,
        string reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        if (reference.Length > 30)
        {
            throw new ArgumentException("Íslandspóstur references cannot exceed 30 characters.", nameof(reference));
        }

        var account = GetAccount(accountReference);
        var result = await GetJsonAsync<ShipmentReferencesResponse>(
            accountReference,
            account,
            BuildUri(account, $"v1/shipmentsByRef/{Uri.EscapeDataString(reference)}/"),
            cancellationToken).ConfigureAwait(false);
        return result?.Shipments ?? throw InvalidResponse("shipment references collection");
    }

    public async Task<IcelandicPostShipmentList> GetShipmentsAsync(
        string accountReference,
        DateTime? startDate = null,
        DateTime? endDate = null,
        CancellationToken cancellationToken = default)
    {
        if (startDate.HasValue && endDate.HasValue && startDate > endDate)
        {
            throw new ArgumentException("Shipment start date cannot be after end date.", nameof(startDate));
        }

        var account = GetAccount(accountReference);
        var format = "yyyy-MM-dd'T'HH:mm:ss";
        return await GetJsonAsync<IcelandicPostShipmentList>(
            accountReference,
            account,
            BuildUri(account, "v1/shipments/", [
                new("startDate", startDate?.ToString(format, CultureInfo.InvariantCulture)),
                new("endDate", endDate?.ToString(format, CultureInfo.InvariantCulture)),
            ]),
            cancellationToken).ConfigureAwait(false)
            ?? throw InvalidResponse("shipments collection");
    }

    public async Task<ShippingLabel> GetLabelAsync(
        string accountReference,
        string shipmentId,
        CancellationToken cancellationToken = default)
    {
        ValidateShipmentId(shipmentId);
        var account = GetAccount(accountReference);
        return await GetDocumentAsync(
            accountReference,
            account,
            BuildUri(account, $"v1/shipments/{Uri.EscapeDataString(shipmentId)}/pdf", [
                new("labelSize", account.LabelFormat.ToString()),
            ]),
            "application/pdf",
            $"posturinn-label-{SafeFileName(shipmentId)}.pdf",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ShippingLabel> GetCombinedLabelsAsync(
        string accountReference,
        IReadOnlyCollection<string> shipmentIds,
        IcelandicPostLabelFormat format = IcelandicPostLabelFormat.A4Size,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shipmentIds);
        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format));
        }

        if (shipmentIds.Count == 0 || shipmentIds.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("At least one valid shipment ID is required.", nameof(shipmentIds));
        }

        var account = GetAccount(accountReference);
        var query = shipmentIds.Select(x => new KeyValuePair<string, string?>("shipmentIds", x)).ToList();
        query.Add(new("format", format.ToString()));
        return await GetDocumentAsync(
            accountReference,
            account,
            BuildUri(account, "v1/shipments/pdfs", query),
            "application/pdf",
            "posturinn-labels.pdf",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ShippingTrackingResult> GetTrackingAsync(
        string accountReference,
        string trackingNumber,
        string language,
        CancellationToken cancellationToken = default)
    {
        var document = await GetShipmentDocumentAsync(
            accountReference,
            trackingNumber,
            language,
            cancellationToken).ConfigureAwait(false);
        return new ShippingTrackingResult(System.Text.Encoding.UTF8.GetString(document.Content));
    }

    public async Task<IcelandicPostDocument> GetProofOfDeliveryAsync(
        string accountReference,
        string shipmentId,
        CancellationToken cancellationToken = default)
    {
        ValidateShipmentId(shipmentId);
        var account = GetAccount(accountReference);
        var document = await GetDocumentAsync(
            accountReference,
            account,
            BuildUri(account, $"v1/shipments/{Uri.EscapeDataString(shipmentId)}/pod"),
            "image/jpeg",
            $"posturinn-pod-{SafeFileName(shipmentId)}.jpg",
            cancellationToken).ConfigureAwait(false);
        return new IcelandicPostDocument(document.Content, document.ContentType, document.FileName);
    }

    public async Task PrintAsync(
        string accountReference,
        string shipmentId,
        int? printerId = null,
        IcelandicPostLabelFormat outputFormat = IcelandicPostLabelFormat.LabelSize9x15,
        CancellationToken cancellationToken = default)
    {
        ValidateShipmentId(shipmentId);
        if (!Enum.IsDefined(outputFormat) || outputFormat == IcelandicPostLabelFormat.Unknown)
        {
            throw new ArgumentOutOfRangeException(nameof(outputFormat), "Unknown is not a valid print output format.");
        }

        var account = GetAccount(accountReference);
        using var request = CreateRequest(
            account,
            HttpMethod.Post,
            BuildRelativePath("v1/print", [
                new("shipmentId", shipmentId),
                new("printerId", printerId?.ToString(CultureInfo.InvariantCulture)),
                new("outputFormat", outputFormat.ToString()),
            ]));
        using var response = await SendMutationAsync(
            request,
            accountReference,
            "print request",
            cancellationToken).ConfigureAwait(false);
    }

    async Task<ShippingShipmentDocument> IShippingShipmentLookupCarrier.GetShipmentAsync(
        string accountReference,
        string shipmentId,
        CancellationToken cancellationToken) =>
        await GetShipmentDocumentAsync(accountReference, shipmentId, "IS", cancellationToken).ConfigureAwait(false);

    async Task IShippingShipmentDeletionCarrier.DeleteShipmentAsync(
        string accountReference,
        string shipmentId,
        CancellationToken cancellationToken) =>
        await DeleteShipmentAsync(accountReference, shipmentId, "IS", cancellationToken).ConfigureAwait(false);

    private async Task<ShippingShipmentDocument> GetShipmentDocumentAsync(
        string accountReference,
        string shipmentId,
        string language,
        CancellationToken cancellationToken)
    {
        ValidateShipmentId(shipmentId);
        ValidateLanguage(language);
        var account = GetAccount(accountReference);
        var content = await GetBytesAsync(
            accountReference,
            account,
            BuildUri(account, $"v1/shipments/{Uri.EscapeDataString(shipmentId)}", [new("language", language)]),
            cancellationToken).ConfigureAwait(false);
        try
        {
            using var _ = JsonDocument.Parse(content);
        }
        catch (JsonException exception)
        {
            throw new ShippingProviderException(Alias, "Íslandspóstur returned invalid shipment data.", exception);
        }

        return new ShippingShipmentDocument(
            content,
            "application/json",
            $"posturinn-shipment-{SafeFileName(shipmentId)}.json");
    }

    private async Task<IReadOnlyList<IcelandicPostPickupLocation>> GetLocationsAsync(
        string accountReference,
        string endpoint,
        string collectionName,
        int? postcode,
        int? maxResults,
        CancellationToken cancellationToken)
    {
        if (postcode is < 100 or > 999)
        {
            throw new ArgumentOutOfRangeException(nameof(postcode), "Postcode must be between 100 and 999.");
        }

        var account = GetAccount(accountReference);
        var cacheKey = $"ekom-shipping:icelandic-post:{accountReference}:{endpoint}:{postcode}:{maxResults}";
        return await GetOrCreateCachedAsync<IReadOnlyList<IcelandicPostPickupLocation>>(
            cacheKey,
            account.CacheDuration,
            async ct =>
            {
                var response = await GetJsonAsync<PickupLocationsResponse>(
                    accountReference,
                    account,
                    BuildUri(account, $"v1/{endpoint}", [
                        new("postcode", postcode?.ToString(CultureInfo.InvariantCulture)),
                        new("maxResults", maxResults?.ToString(CultureInfo.InvariantCulture)),
                    ]),
                    ct).ConfigureAwait(false);
                var locations = (collectionName == "postboxes" ? response?.Postboxes : response?.ParcelPoints)
                    ?? throw InvalidResponse($"{collectionName} collection");
                return locations.Select(x => x.ToLocation()).ToArray();
            },
            cancellationToken).ConfigureAwait(false);
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

    private async Task<T?> GetJsonAsync<T>(
        string accountReference,
        IcelandicPostAccountOptions account,
        Uri uri,
        CancellationToken cancellationToken)
    {
        var content = await GetBytesAsync(accountReference, account, uri, cancellationToken).ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize<T>(content, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new ShippingProviderException(Alias, "Íslandspóstur returned an invalid response.", exception);
        }
    }

    private async Task<byte[]> GetBytesAsync(
        string accountReference,
        IcelandicPostAccountOptions account,
        Uri uri,
        CancellationToken cancellationToken,
        string accept = "application/json")
    {
        using var request = CreateRequest(account, HttpMethod.Get, uri, accept);
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, accountReference, "request");
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (accept != "application/json" &&
            !string.Equals(mediaType, accept, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(mediaType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            throw new ShippingProviderException(
                Alias,
                $"Íslandspóstur returned '{mediaType ?? "an unknown content type"}' instead of '{accept}'.");
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ShippingLabel> GetDocumentAsync(
        string accountReference,
        IcelandicPostAccountOptions account,
        Uri uri,
        string contentType,
        string fileName,
        CancellationToken cancellationToken)
    {
        var content = await GetBytesAsync(accountReference, account, uri, cancellationToken, contentType).ConfigureAwait(false);
        if (content.Length == 0)
        {
            throw new ShippingProviderException(Alias, "Íslandspóstur returned an empty document.");
        }

        return new ShippingLabel(content, contentType, fileName);
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ShipmentOutcomeUnknownException(
                $"Íslandspóstur {operation} ended without a definitive response.",
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
            throw new ShippingProviderException(Alias, $"Íslandspóstur {operation} failed with HTTP status {statusCode}.");
        }

        _logger.LogWarning(
            "Íslandspóstur {Operation} had an uncertain outcome for account {AccountReference} with status {StatusCode}",
            operation,
            accountReference,
            statusCode);
        throw new ShipmentOutcomeUnknownException(
            $"Íslandspóstur {operation} returned HTTP status {statusCode}; its outcome is uncertain.",
            new HttpRequestException($"Íslandspóstur returned HTTP status {statusCode}."));
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        await _httpClientFactory.CreateClient(nameof(IcelandicPostCarrier))
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

    private HttpRequestMessage CreateRequest(
        IcelandicPostAccountOptions account,
        HttpMethod method,
        string path) => CreateRequest(account, method, BuildUri(account, path));

    private static HttpRequestMessage CreateRequest(
        IcelandicPostAccountOptions account,
        HttpMethod method,
        Uri uri,
        string accept = "application/json")
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Add("x-api-key", account.ApiKey);
        request.Headers.Accept.ParseAdd(accept);
        return request;
    }

    private void EnsureSuccess(HttpResponseMessage response, string accountReference, string operation)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        _logger.LogWarning(
            "Íslandspóstur {Operation} failed for account {AccountReference} with status {StatusCode}",
            operation,
            accountReference,
            (int)response.StatusCode);
        throw new ShippingProviderException(
            Alias,
            $"Íslandspóstur {operation} failed with HTTP status {(int)response.StatusCode}.");
    }

    private IcelandicPostAccountOptions GetAccount(string accountReference)
    {
        if (string.IsNullOrWhiteSpace(accountReference) ||
            !_options.Accounts.TryGetValue(accountReference, out var account))
        {
            throw new ShippingConfigurationException($"Íslandspóstur account '{accountReference}' is not configured.");
        }

        if (string.IsNullOrWhiteSpace(account.ApiKey))
        {
            throw new ShippingConfigurationException($"Íslandspóstur account '{accountReference}' has incomplete credentials.");
        }

        _ = BuildUri(account, string.Empty);
        return account;
    }

    private static Uri BuildUri(
        IcelandicPostAccountOptions account,
        string path,
        IEnumerable<KeyValuePair<string, string?>>? query = null) =>
        new(ValidateBaseUri(account.ApiUrl), BuildRelativePath(path, query));

    private static string BuildRelativePath(
        string path,
        IEnumerable<KeyValuePair<string, string?>>? query = null)
    {
        var values = query?
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value!)}")
            .ToArray();
        return values is { Length: > 0 } ? $"{path}?{string.Join('&', values)}" : path;
    }

    private static Uri ValidateBaseUri(string apiUrl)
    {
        if (!Uri.TryCreate(apiUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ShippingConfigurationException("Íslandspóstur accounts must use an absolute HTTPS API URL.");
        }

        return uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal) ? uri : new Uri(uri.AbsoluteUri + '/');
    }

    private static void ValidateShipmentRequest(IcelandicPostShipmentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Recipient);
        ArgumentNullException.ThrowIfNull(request.Options);
        if (string.IsNullOrWhiteSpace(request.Recipient.Name) ||
            string.IsNullOrWhiteSpace(request.Recipient.AddressLine1) ||
            string.IsNullOrWhiteSpace(request.Recipient.Postcode) ||
            string.IsNullOrWhiteSpace(request.Recipient.CountryCode) ||
            string.IsNullOrWhiteSpace(request.Options.DeliveryServiceId))
        {
            throw new ArgumentException("Shipment recipient and delivery service fields are incomplete.", nameof(request));
        }

        if (!string.Equals(request.Recipient.CountryCode, "IS", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.Recipient.Town) || string.IsNullOrWhiteSpace(request.Recipient.Email))
            {
                throw new ArgumentException("International shipments require recipient town and email.", nameof(request));
            }

            if (request.Items is { Count: > 1 })
            {
                throw new ArgumentException("International shipments can contain only one parcel.", nameof(request));
            }

            if (request.Contents is not { Count: > 0 })
            {
                throw new ArgumentException("International shipments require customs contents.", nameof(request));
            }
        }

        if (request.Options.Reference?.Length > 30)
        {
            throw new ArgumentException("Shipment references cannot exceed 30 characters.", nameof(request));
        }
    }

    private static bool RequiresPickupSelection(string serviceId) =>
        serviceId.Equals("DPO", StringComparison.OrdinalIgnoreCase) ||
        serviceId.Equals("DNO", StringComparison.OrdinalIgnoreCase) ||
        serviceId.Equals("DPT", StringComparison.OrdinalIgnoreCase) ||
        serviceId.Equals("DPP", StringComparison.OrdinalIgnoreCase);

    private static void ValidateShipmentId(string shipmentId) =>
        ArgumentException.ThrowIfNullOrWhiteSpace(shipmentId);

    private static void ValidateLanguage(string language)
    {
        if (!language.Equals("IS", StringComparison.OrdinalIgnoreCase) &&
            !language.Equals("EN", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentOutOfRangeException(nameof(language), "Language must be IS or EN.");
        }
    }

    private static ShippingProviderException InvalidResponse(string value) =>
        new(IcelandicPostShippingDefaults.CarrierAlias, $"Íslandspóstur returned no {value}.");

    private static string SafeFileName(string value) =>
        string.Concat(value.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

    private sealed class DeliveryServicesResponse
    {
        public IcelandicPostDeliveryService[]? DeliveryServicesAndPrices { get; init; }
    }

    private sealed class PickupLocationsResponse
    {
        public PickupLocationResponse[]? Postboxes { get; init; }
        public PickupLocationResponse[]? ParcelPoints { get; init; }
    }

    private sealed class PostOfficesResponse
    {
        public PickupLocationResponse[]? PostOffices { get; init; }
    }

    private sealed class PickupLocationResponse
    {
        public string PostboxId { get; init; } = string.Empty;
        public string ParcelPointId { get; init; } = string.Empty;
        public string PostOfficeId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Address { get; init; } = string.Empty;
        public string Postcode { get; init; } = string.Empty;
        public string Town { get; init; } = string.Empty;
        public string? Latitude { get; init; }
        public string? Longitude { get; init; }

        public IcelandicPostPickupLocation ToLocation()
        {
            var id = new[] { PostboxId, ParcelPointId, PostOfficeId }.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                ?? string.Empty;
            return new IcelandicPostPickupLocation(
                id,
                string.IsNullOrWhiteSpace(Name) ? $"Pósthús {Postcode}" : Name,
                Address,
                Postcode,
                Town,
                ParseCoordinate(Latitude),
                ParseCoordinate(Longitude));
        }

        private static double? ParseCoordinate(string? value) =>
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var coordinate)
                ? coordinate
                : null;
    }

    private sealed class ShipmentReferencesResponse
    {
        public IcelandicPostShipmentReference[]? Shipments { get; init; }
    }
}
