using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ekom.Shipping.DhlExpress;

internal sealed class DhlExpressCarrier :
    IDhlExpressShippingService,
    IShippingCarrierRequiresDocumentStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DhlExpressOptions _options;
    private readonly IReadOnlyList<IDhlExpressShipmentEnricher> _enrichers;
    private readonly ILogger<DhlExpressCarrier> _logger;

    public DhlExpressCarrier(
        IHttpClientFactory httpClientFactory,
        IOptions<DhlExpressOptions> options,
        IEnumerable<IDhlExpressShipmentEnricher> enrichers,
        ILogger<DhlExpressCarrier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _enrichers = enrichers.ToArray();
        _logger = logger;
    }

    public string Alias => DhlExpressDefaults.CarrierAlias;

    public ShippingCarrierCapabilities Capabilities =>
        ShippingCarrierCapabilities.Services |
        ShippingCarrierCapabilities.ShipmentBooking |
        ShippingCarrierCapabilities.Labels |
        ShippingCarrierCapabilities.Tracking |
        ShippingCarrierCapabilities.ShipmentLookup;

    public ShippingFulfillmentMode GetFulfillmentMode(string accountReference) =>
        GetAccount(accountReference).FulfillmentMode;

    public Task<IReadOnlyList<ShippingService>> GetServicesAsync(
        string accountReference,
        ShippingLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var account = GetAccount(accountReference);
        IReadOnlyList<ShippingService> services =
        [
            new ShippingService(account.ProductCode, account.ProductName, RequiresPickupLocation: false),
        ];
        return Task.FromResult(services);
    }

    public Task<IReadOnlyList<PickupLocation>> GetPickupLocationsAsync(
        string accountReference,
        string serviceId,
        ShippingLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        _ = GetAccount(accountReference);
        return Task.FromResult<IReadOnlyList<PickupLocation>>([]);
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
        if (!string.IsNullOrWhiteSpace(request.PickupLocationId))
        {
            throw new InvalidShippingSelectionException(
                "DHL Express fulfillment does not map Unified Location Finder IDs to Express service-point IDs.");
        }

        if (_enrichers.Count != 1)
        {
            throw new ShippingConfigurationException(
                _enrichers.Count == 0
                    ? "DHL Express fulfillment requires one IDhlExpressShipmentEnricher implementation."
                    : "DHL Express fulfillment supports only one IDhlExpressShipmentEnricher implementation.");
        }

        var account = GetAccount(accountReference);
        if (!string.Equals(request.ServiceId, account.ProductCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidShippingSelectionException(
                $"DHL Express service '{request.ServiceId}' does not match configured product '{account.ProductCode}'.");
        }

        var shipmentRequest = await _enrichers[0].EnrichAsync(
            new DhlExpressShipmentEnrichmentContext(
                accountReference,
                account.AccountNumber,
                account.ProductCode,
                ToShipper(account.Shipper),
                request),
            cancellationToken).ConfigureAwait(false);
        if (!string.Equals(shipmentRequest.ProductCode, request.ServiceId, StringComparison.OrdinalIgnoreCase) ||
            !shipmentRequest.Accounts.Any(candidate =>
                string.Equals(candidate.TypeCode, "shipper", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.Number, account.AccountNumber, StringComparison.Ordinal)))
        {
            throw new ShippingConfigurationException(
                "The DHL enricher must preserve the selected product and configured shipper account.");
        }

        if (shipmentRequest.OutputImageProperties?.ImageOptions?.Any(option =>
                string.Equals(option.TypeCode, "label", StringComparison.OrdinalIgnoreCase) &&
                option.IsRequested != false) != true)
        {
            throw new ShippingConfigurationException("DHL Ekom fulfillment must request a transport label.");
        }

        var response = await CreateShipmentAsync(accountReference, shipmentRequest, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        try
        {
            if (string.IsNullOrWhiteSpace(response.ShipmentTrackingNumber))
            {
                throw new JsonException("shipmentTrackingNumber was empty.");
            }

            var sourceDocuments = response.Documents
                ?? throw new JsonException("documents was null.");
            var documents = sourceDocuments.Select((document, index) =>
            {
                if (string.IsNullOrWhiteSpace(document.TypeCode) ||
                    string.IsNullOrWhiteSpace(document.ImageFormat) ||
                    string.IsNullOrWhiteSpace(document.Content))
                {
                    throw new JsonException("A DHL creation document was incomplete.");
                }

                return new ShippingCreationDocument(
                    document.TypeCode,
                    document.ImageFormat,
                    Convert.FromBase64String(document.Content),
                    MediaType(document.ImageFormat),
                    FileName(response.ShipmentTrackingNumber, document, index),
                    document.PackageReferenceNumber);
            }).ToArray();

            if (!documents.Any(document => string.Equals(document.TypeCode, "label", StringComparison.OrdinalIgnoreCase)))
            {
                throw new JsonException("No label document was present.");
            }

            return new ShipmentBookingResult(
                response.ShipmentTrackingNumber,
                request.MerchantOrderId,
                response.ShipmentTrackingNumber,
                documents);
        }
        catch (Exception exception) when (exception is not ShipmentOutcomeUnknownException)
        {
            throw new ShipmentOutcomeUnknownException(
                "DHL created the shipment but its creation response could not be safely persisted.",
                exception);
        }
    }

    public Task<ShippingLabel> GetLabelAsync(
        string accountReference,
        string shipmentId,
        CancellationToken cancellationToken = default)
    {
        _ = GetAccount(accountReference);
        throw new ShippingException(
            "DHL Express transport labels are available only from the private document store populated during shipment creation.");
    }

    public async Task<ShippingTrackingResult> GetTrackingAsync(
        string accountReference,
        string trackingNumber,
        string language,
        CancellationToken cancellationToken = default)
    {
        var response = await GetShipmentTrackingAsync(
            accountReference,
            trackingNumber,
            language: NormalizeLanguage(language),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return new ShippingTrackingResult(JsonSerializer.Serialize(response, JsonOptions));
    }

    async Task<ShippingShipmentDocument> IShippingShipmentLookupCarrier.GetShipmentAsync(
        string accountReference,
        string shipmentId,
        CancellationToken cancellationToken)
    {
        var response = await GetShipmentTrackingAsync(
            accountReference,
            shipmentId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return new ShippingShipmentDocument(
            JsonSerializer.SerializeToUtf8Bytes(response, JsonOptions),
            "application/json",
            $"dhl-express-shipment-{SafeFileName(shipmentId)}.json");
    }

    public Task<DhlExpressRatesResponse> GetRatesAsync(
        string accountReference,
        DhlExpressRateRequest request,
        bool strictValidation = false,
        CancellationToken cancellationToken = default) =>
        SendJsonAsync<DhlExpressRatesResponse>(
            accountReference,
            HttpMethod.Post,
            $"rates?strictValidation={strictValidation.ToString().ToLowerInvariant()}",
            request,
            cancellationToken);

    public Task<DhlExpressProductsResponse> GetProductsAsync(
        string accountReference,
        DhlExpressOnePieceRequest request,
        CancellationToken cancellationToken = default) =>
        SendJsonAsync<DhlExpressProductsResponse>(
            accountReference,
            HttpMethod.Get,
            BuildPath("products", OnePieceQuery(request)),
            null,
            cancellationToken);

    public Task<DhlExpressAddressValidationResponse> ValidateAddressAsync(
        string accountReference,
        DhlExpressAddressValidationRequest request,
        CancellationToken cancellationToken = default) =>
        SendJsonAsync<DhlExpressAddressValidationResponse>(
            accountReference,
            HttpMethod.Get,
            BuildPath("address-validate", [
                new("type", request.Type),
                new("countryCode", request.CountryCode),
                new("postalCode", request.PostalCode),
                new("cityName", request.CityName),
                new("countyName", request.CountyName),
                new("strictValidation", request.StrictValidation?.ToString().ToLowerInvariant()),
            ]),
            null,
            cancellationToken);

    public async Task<DhlExpressShipmentResponse> CreateShipmentAsync(
        string accountReference,
        DhlExpressShipmentRequest request,
        bool validateDataOnly = false,
        CancellationToken cancellationToken = default)
    {
        ValidateShipment(request);
        var account = GetAccount(accountReference);
        using var httpRequest = CreateRequest(
            account,
            HttpMethod.Post,
            $"shipments?validateDataOnly={validateDataOnly.ToString().ToLowerInvariant()}");
        httpRequest.Content = JsonContent.Create(request, options: JsonOptions);
        using var response = await SendMutationAsync(httpRequest, accountReference, "shipment creation", cancellationToken)
            .ConfigureAwait(false);
        return await ReadMutationResponseAsync<DhlExpressShipmentResponse>(response, "shipment creation", cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<DhlExpressTrackingResponse> GetShipmentTrackingAsync(
        string accountReference,
        string shipmentTrackingNumber,
        string trackingView = "all-checkpoints",
        string levelOfDetail = "shipment",
        string language = "eng",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shipmentTrackingNumber);
        return SendJsonAsync<DhlExpressTrackingResponse>(
            accountReference,
            HttpMethod.Get,
            BuildPath($"shipments/{Uri.EscapeDataString(shipmentTrackingNumber)}/tracking", [
                new("trackingView", trackingView),
                new("levelOfDetail", levelOfDetail),
            ]),
            null,
            cancellationToken,
            NormalizeLanguage(language));
    }

    public Task<DhlExpressDocumentResponse> GetProofOfDeliveryAsync(
        string accountReference,
        string shipmentTrackingNumber,
        string content = "epod-summary",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shipmentTrackingNumber);
        var account = GetAccount(accountReference);
        return SendJsonAsync<DhlExpressDocumentResponse>(
            accountReference,
            HttpMethod.Get,
            BuildPath($"shipments/{Uri.EscapeDataString(shipmentTrackingNumber)}/proof-of-delivery", [
                new("shipperAccountNumber", account.AccountNumber),
                new("content", content),
            ]),
            null,
            cancellationToken);
    }

    public Task<DhlExpressDocumentImageResponse> GetImageAsync(
        string accountReference,
        string shipmentTrackingNumber,
        DhlExpressImageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shipmentTrackingNumber);
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ShipperAccountNumber) && string.IsNullOrWhiteSpace(request.PayerAccountNumber))
        {
            throw new ArgumentException("A shipper or payer account number is required.", nameof(request));
        }

        return SendJsonAsync<DhlExpressDocumentImageResponse>(
            accountReference,
            HttpMethod.Get,
            BuildPath($"shipments/{Uri.EscapeDataString(shipmentTrackingNumber)}/get-image", [
                new("shipperAccountNumber", request.ShipperAccountNumber),
                new("payerAccountNumber", request.PayerAccountNumber),
                new("typeCode", request.TypeCode),
                new("pickupYearAndMonth", request.PickupYearAndMonth),
                new("encodingFormat", request.EncodingFormat),
                new("allInOnePDF", request.AllInOnePDF?.ToString().ToLowerInvariant()),
                new("compressedPackage", request.CompressedPackage?.ToString().ToLowerInvariant()),
            ]),
            null,
            cancellationToken);
    }

    public Task UploadImagesAsync(
        string accountReference,
        string shipmentTrackingNumber,
        DhlExpressImageUploadRequest request,
        CancellationToken cancellationToken = default) =>
        SendMutationWithoutResponseAsync(
            accountReference,
            HttpMethod.Patch,
            $"shipments/{Uri.EscapeDataString(shipmentTrackingNumber)}/upload-image",
            request,
            "image upload",
            cancellationToken);

    public Task UploadInvoiceDataAsync(
        string accountReference,
        string shipmentTrackingNumber,
        DhlExpressInvoiceDataRequest request,
        CancellationToken cancellationToken = default) =>
        SendMutationWithoutResponseAsync(
            accountReference,
            HttpMethod.Patch,
            $"shipments/{Uri.EscapeDataString(shipmentTrackingNumber)}/upload-invoice-data",
            request,
            "invoice-data upload",
            cancellationToken);

    public async Task<DhlExpressPickupResponse> CreatePickupAsync(
        string accountReference,
        DhlExpressPickupRequest request,
        CancellationToken cancellationToken = default) =>
        await SendMutationWithResponseAsync<DhlExpressPickupResponse>(
            accountReference,
            HttpMethod.Post,
            "pickups",
            request,
            "pickup creation",
            cancellationToken).ConfigureAwait(false);

    public async Task<DhlExpressPickupUpdateResponse> UpdatePickupAsync(
        string accountReference,
        string dispatchConfirmationNumber,
        DhlExpressPickupUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dispatchConfirmationNumber);
        if (!string.Equals(dispatchConfirmationNumber, request.DispatchConfirmationNumber, StringComparison.Ordinal))
        {
            throw new ArgumentException("The pickup confirmation number must match the path value.", nameof(request));
        }

        return await SendMutationWithResponseAsync<DhlExpressPickupUpdateResponse>(
            accountReference,
            HttpMethod.Patch,
            $"pickups/{Uri.EscapeDataString(dispatchConfirmationNumber)}",
            request,
            "pickup update",
            cancellationToken).ConfigureAwait(false);
    }

    public Task CancelPickupAsync(
        string accountReference,
        string dispatchConfirmationNumber,
        string requestorName,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dispatchConfirmationNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestorName);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return SendMutationWithoutResponseAsync<object?>(
            accountReference,
            HttpMethod.Delete,
            BuildPath($"pickups/{Uri.EscapeDataString(dispatchConfirmationNumber)}", [
                new("requestorName", requestorName),
                new("reason", reason),
            ]),
            null,
            "pickup cancellation",
            cancellationToken);
    }

    private async Task<T> SendJsonAsync<T>(
        string accountReference,
        HttpMethod method,
        string path,
        object? content,
        CancellationToken cancellationToken,
        string? acceptLanguage = null)
    {
        var account = GetAccount(accountReference);
        using var request = CreateRequest(account, method, path);
        if (!string.IsNullOrWhiteSpace(acceptLanguage))
        {
            request.Headers.AcceptLanguage.ParseAdd(acceptLanguage);
        }
        if (content is not null)
        {
            request.Content = JsonContent.Create(content, options: JsonOptions);
        }

        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, accountReference, "request");
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false)
                ?? throw new JsonException("The response body was empty.");
        }
        catch (JsonException exception)
        {
            throw new ShippingProviderException(Alias, "DHL Express returned invalid response data.", exception);
        }
    }

    private async Task<T> SendMutationWithResponseAsync<T>(
        string accountReference,
        HttpMethod method,
        string path,
        object content,
        string operation,
        CancellationToken cancellationToken)
    {
        var account = GetAccount(accountReference);
        using var request = CreateRequest(account, method, path);
        request.Content = JsonContent.Create(content, options: JsonOptions);
        using var response = await SendMutationAsync(request, accountReference, operation, cancellationToken)
            .ConfigureAwait(false);
        return await ReadMutationResponseAsync<T>(response, operation, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendMutationWithoutResponseAsync<T>(
        string accountReference,
        HttpMethod method,
        string path,
        T content,
        string operation,
        CancellationToken cancellationToken)
    {
        var account = GetAccount(accountReference);
        using var request = CreateRequest(account, method, path);
        if (content is not null)
        {
            request.Content = JsonContent.Create(content, options: JsonOptions);
        }

        using var response = await SendMutationAsync(request, accountReference, operation, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<T> ReadMutationResponseAsync<T>(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false)
                ?? throw new JsonException("The response body was empty.");
        }
        catch (Exception exception)
        {
            throw new ShipmentOutcomeUnknownException(
                $"DHL Express {operation} succeeded but returned an invalid response.",
                exception);
        }
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
            throw new ShipmentOutcomeUnknownException($"DHL Express {operation} had an uncertain outcome.", exception);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        var statusCode = (int)response.StatusCode;
        response.Dispose();
        if (statusCode is >= 400 and < 500 && statusCode is not 408 and not 409 and not 429)
        {
            throw new ShippingProviderException(Alias, $"DHL Express {operation} failed with HTTP status {statusCode}.");
        }

        _logger.LogWarning(
            "DHL Express {Operation} had an uncertain outcome for account {AccountReference} with status {StatusCode}",
            operation,
            accountReference,
            statusCode);
        throw new ShipmentOutcomeUnknownException(
            $"DHL Express {operation} returned HTTP status {statusCode}; its outcome is uncertain.",
            new HttpRequestException($"DHL Express returned HTTP status {statusCode}."));
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        await _httpClientFactory.CreateClient(nameof(DhlExpressCarrier))
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

    private HttpRequestMessage CreateRequest(DhlExpressAccountOptions account, HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(ValidateBaseUri(account.ApiUrl), path));
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{account.Username}:{account.Password}")));
        request.Headers.Add("x-version", account.ApiVersion);
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
            "DHL Express {Operation} failed for account {AccountReference} with status {StatusCode}",
            operation,
            accountReference,
            (int)response.StatusCode);
        throw new ShippingProviderException(
            Alias,
            $"DHL Express {operation} failed with HTTP status {(int)response.StatusCode}.");
    }

    private DhlExpressAccountOptions GetAccount(string accountReference)
    {
        if (string.IsNullOrWhiteSpace(accountReference) || !_options.Accounts.TryGetValue(accountReference, out var account))
        {
            throw new ShippingConfigurationException($"DHL Express account '{accountReference}' is not configured.");
        }

        return account;
    }

    private static DhlExpressParty ToShipper(DhlExpressShipperOptions shipper) =>
        new(
            new DhlExpressAddress(
                shipper.PostalCode,
                shipper.CityName,
                shipper.CountryCode,
                shipper.AddressLine1,
                shipper.ProvinceCode,
                shipper.AddressLine2),
            new DhlExpressContact(
                shipper.Phone,
                shipper.CompanyName,
                shipper.Name,
                string.IsNullOrWhiteSpace(shipper.Email) ? null : shipper.Email));

    private static void ValidateShipment(DhlExpressShipmentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.PlannedShippingDateAndTime) ||
            string.IsNullOrWhiteSpace(request.ProductCode) ||
            request.Accounts.Count == 0 ||
            request.Content.Packages.Count == 0)
        {
            throw new ArgumentException("DHL shipment timing, product, account, and package data are required.", nameof(request));
        }

        if (request.Content.Packages.Any(package =>
                package.Weight <= 0 ||
                package.Dimensions is { Length: <= 0 } or { Width: <= 0 } or { Height: <= 0 }))
        {
            throw new ArgumentException("DHL package weights and supplied dimensions must be positive.", nameof(request));
        }

        if (request.Content.IsCustomsDeclarable &&
            (request.Content.ExportDeclaration?.LineItems.Count is not > 0 ||
             request.Content.DeclaredValue is null ||
             string.IsNullOrWhiteSpace(request.Content.DeclaredValueCurrency)))
        {
            throw new ArgumentException(
                "Customs-declarable DHL shipments require declared value, currency, and export declaration lines.",
                nameof(request));
        }

        if (request.Content.ExportDeclaration?.LineItems.Any(line =>
                line.Weight.NetValue is null && line.Weight.GrossValue is null) == true)
        {
            throw new ArgumentException("Each DHL customs line requires net or gross weight.", nameof(request));
        }
    }

    private static IReadOnlyList<KeyValuePair<string, string?>> OnePieceQuery(DhlExpressOnePieceRequest request) =>
    [
        new("accountNumber", request.AccountNumber),
        new("originCountryCode", request.OriginCountryCode),
        new("originCityName", request.OriginCityName),
        new("originPostalCode", request.OriginPostalCode),
        new("destinationCountryCode", request.DestinationCountryCode),
        new("destinationCityName", request.DestinationCityName),
        new("destinationPostalCode", request.DestinationPostalCode),
        new("weight", request.Weight.ToString(CultureInfo.InvariantCulture)),
        new("length", request.Length.ToString(CultureInfo.InvariantCulture)),
        new("width", request.Width.ToString(CultureInfo.InvariantCulture)),
        new("height", request.Height.ToString(CultureInfo.InvariantCulture)),
        new("plannedShippingDate", request.PlannedShippingDate),
        new("isCustomsDeclarable", request.IsCustomsDeclarable.ToString().ToLowerInvariant()),
        new("unitOfMeasurement", request.UnitOfMeasurement),
        new("nextBusinessDay", request.NextBusinessDay?.ToString().ToLowerInvariant()),
        new("strictValidation", request.StrictValidation?.ToString().ToLowerInvariant()),
        new("getAllValueAddedServices", request.GetAllValueAddedServices?.ToString().ToLowerInvariant()),
        new("requestEstimatedDeliveryDate", request.RequestEstimatedDeliveryDate?.ToString().ToLowerInvariant()),
        new("estimatedDeliveryDateType", request.EstimatedDeliveryDateType),
    ];

    private static string BuildPath(string path, IEnumerable<KeyValuePair<string, string?>> query)
    {
        var values = query
            .Where(value => !string.IsNullOrWhiteSpace(value.Value))
            .Select(value => $"{Uri.EscapeDataString(value.Key)}={Uri.EscapeDataString(value.Value!)}");
        var suffix = string.Join('&', values);
        return string.IsNullOrEmpty(suffix) ? path : $"{path}?{suffix}";
    }

    private static Uri ValidateBaseUri(string apiUrl)
    {
        if (!Uri.TryCreate(apiUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ShippingConfigurationException("DHL Express accounts must use an absolute HTTPS API URL.");
        }

        return uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal) ? uri : new Uri(uri.AbsoluteUri + '/');
    }

    private static string MediaType(string format) => format.ToLowerInvariant() switch
    {
        "pdf" => "application/pdf",
        "png" => "image/png",
        "gif" => "image/gif",
        "tiff" or "tif" => "image/tiff",
        "jpeg" or "jpg" => "image/jpeg",
        _ => "application/octet-stream",
    };

    private static string FileName(string shipmentId, DhlExpressDocument document, int index)
    {
        var extension = document.ImageFormat.ToLowerInvariant() switch
        {
            "jpeg" => "jpg",
            "tiff" => "tif",
            var value => value,
        };
        var piece = document.PackageReferenceNumber is null ? string.Empty : $"-{document.PackageReferenceNumber}";
        return $"dhl-{SafeFileName(shipmentId)}-{SafeFileName(document.TypeCode)}{piece}-{index + 1}.{extension}";
    }

    private static string SafeFileName(string value) =>
        string.Concat(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

    private static string NormalizeLanguage(string language) => language.ToUpperInvariant() switch
    {
        "EN" => "eng",
        "IS" => "isl",
        _ when language.Length == 3 => language.ToLowerInvariant(),
        _ => throw new ArgumentOutOfRangeException(nameof(language), "DHL language must be an ISO 639 code."),
    };
}
