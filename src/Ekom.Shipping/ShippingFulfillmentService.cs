using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using Ekom.API;
using Ekom.Models;
using Ekom.Services;
using Microsoft.Extensions.Logging;

namespace Ekom.Shipping.Ekom;

internal sealed class ShippingFulfillmentService : IShippingFulfillmentService
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> OrderLocks = new();
    private readonly global::Ekom.API.Order _orderApi;
    private readonly IShippingFulfillmentCarrierRegistry _carriers;
    private readonly IShippingOrderMapper _mapper;
    private readonly IEnumerable<IShippingAutomationRule> _automationRules;
    private readonly IOrderActivityLogService _activityLog;
    private readonly ILogger<ShippingFulfillmentService> _logger;

    public ShippingFulfillmentService(
        global::Ekom.API.Order orderApi,
        IShippingFulfillmentCarrierRegistry carriers,
        IShippingOrderMapper mapper,
        IEnumerable<IShippingAutomationRule> automationRules,
        IOrderActivityLogService activityLog,
        ILogger<ShippingFulfillmentService> logger)
    {
        _orderApi = orderApi;
        _carriers = carriers;
        _mapper = mapper;
        _automationRules = automationRules;
        _activityLog = activityLog;
        _logger = logger;
    }

    public async Task<ShippingFulfillmentRecord?> CreateAutomaticAsync(
        IOrderInfo order,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        var configuration = EkomShippingMethodConfiguration.FromProperties(
            order.ShippingProvider.Key,
            order.ShippingProvider.Properties);
        if (configuration is null)
        {
            return null;
        }

        var carrier = _carriers.Carriers.FirstOrDefault(candidate =>
            string.Equals(candidate.Alias, configuration.CarrierAlias, StringComparison.OrdinalIgnoreCase));
        if (carrier is null ||
            carrier.GetFulfillmentMode(configuration.AccountReference) != ShippingFulfillmentMode.Automatic)
        {
            return null;
        }

        foreach (var rule in _automationRules)
        {
            if (!await rule.CanCreateShipmentAsync(order, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }
        }

        return await CreateInternalAsync(order, retryFailed: false, isEventHandler: true, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ShippingFulfillmentRecord> CreateAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrderAsync(orderId, cancellationToken).ConfigureAwait(false);
        return await CreateInternalAsync(order, retryFailed: false, isEventHandler: false, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ShippingFulfillmentRecord> RetryAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrderAsync(orderId, cancellationToken).ConfigureAwait(false);
        return await CreateInternalAsync(order, retryFailed: true, isEventHandler: false, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ShippingFulfillmentRecord?> GetAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await _orderApi.GetOrderAsync(orderId, cancellationToken).ConfigureAwait(false);
        return order is null ? null : ReadRecord(order);
    }

    public async Task<ShippingLabel> GetLabelAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrderAsync(orderId, cancellationToken).ConfigureAwait(false);
        var record = ReadRecord(order)
            ?? throw new ShippingException("This order does not have a shipment.");
        if (record.State != ShippingFulfillmentState.Created || string.IsNullOrWhiteSpace(record.ShipmentId))
        {
            throw new ShippingException("A label is only available after shipment creation.");
        }

        var carrier = _carriers.GetRequired(record.CarrierAlias);
        return await carrier.GetLabelAsync(
            record.AccountReference,
            record.ShipmentId,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ShippingFulfillmentRecord> CreateInternalAsync(
        IOrderInfo initialOrder,
        bool retryFailed,
        bool isEventHandler,
        CancellationToken cancellationToken)
    {
        var semaphore = OrderLocks.GetOrAdd(initialOrder.UniqueId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var order = await _orderApi.GetOrderAsync(initialOrder.UniqueId, cancellationToken)
                .ConfigureAwait(false) ?? initialOrder;
            var configuration = EkomShippingMethodConfiguration.FromProperties(
                order.ShippingProvider.Key,
                order.ShippingProvider.Properties)
                ?? throw new ShippingConfigurationException("The order shipping method has no carrier configuration.");
            var existing = ReadRecord(order);
            if (existing?.State == ShippingFulfillmentState.Created)
            {
                return existing;
            }

            var staleBefore = DateTime.UtcNow.AddMinutes(-10);
            if (existing?.State == ShippingFulfillmentState.Submitting && existing.UpdatedUtc < staleBefore)
            {
                order = await SaveStateAsync(
                    order,
                    configuration,
                    ShippingFulfillmentState.OutcomeUnknown,
                    existing.AttemptCount,
                    existing.BookingReference,
                    existing.ShipmentId,
                    existing.TrackingNumber,
                    "Shipment submission was interrupted and requires reconciliation.",
                    isEventHandler,
                    CancellationToken.None).ConfigureAwait(false);
                return ReadRecord(order)!;
            }

            if ((existing?.State == ShippingFulfillmentState.Processing && existing.UpdatedUtc >= staleBefore) ||
                existing?.State is ShippingFulfillmentState.Submitting or ShippingFulfillmentState.OutcomeUnknown)
            {
                throw new ShippingException(
                    $"Shipment creation cannot continue while its state is {existing.State}.");
            }

            if (existing?.State == ShippingFulfillmentState.Failed && !retryFailed)
            {
                throw new ShippingException("The previous shipment attempt failed. Use the explicit retry operation.");
            }

            if (retryFailed && existing?.State != ShippingFulfillmentState.Failed)
            {
                throw new ShippingException("Only a shipment with state Failed can be retried.");
            }

            var carrier = _carriers.GetRequired(configuration.CarrierAlias);
            var attempts = (existing?.AttemptCount ?? 0) + 1;
            var bookingReference = existing?.BookingReference;
            order = await SaveStateAsync(
                order,
                configuration,
                ShippingFulfillmentState.Processing,
                attempts,
                bookingReference,
                null,
                null,
                null,
                isEventHandler,
                cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(bookingReference))
            {
                try
                {
                    bookingReference = await carrier.ReserveBookingReferenceAsync(
                        configuration.AccountReference,
                        cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(bookingReference))
                    {
                        order = await SaveStateAsync(
                            order,
                            configuration,
                            ShippingFulfillmentState.Processing,
                            attempts,
                            bookingReference,
                            null,
                            null,
                            null,
                            isEventHandler,
                            cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    return await SaveFailureAsync(
                        order,
                        configuration,
                        ShippingFulfillmentState.Failed,
                        attempts,
                        bookingReference,
                        null,
                        null,
                        exception,
                        isEventHandler).ConfigureAwait(false);
                }
            }

            ShipmentBookingRequest request;
            try
            {
                request = _mapper.Map(order, configuration, bookingReference);
                order = await SaveStateAsync(
                    order,
                    configuration,
                    ShippingFulfillmentState.Submitting,
                    attempts,
                    bookingReference,
                    null,
                    null,
                    null,
                    isEventHandler,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return await SaveFailureAsync(
                    order,
                    configuration,
                    ShippingFulfillmentState.Failed,
                    attempts,
                    bookingReference,
                    null,
                    null,
                    exception,
                    isEventHandler).ConfigureAwait(false);
            }

            ShipmentBookingResult result;
            try
            {
                result = await carrier.CreateShipmentAsync(
                    configuration.AccountReference,
                    request,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (ShipmentOutcomeUnknownException exception)
            {
                return await SaveFailureAsync(
                    order,
                    configuration,
                    ShippingFulfillmentState.OutcomeUnknown,
                    attempts,
                    bookingReference,
                    null,
                    null,
                    exception,
                    isEventHandler).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
            {
                await SaveFailureAsync(
                    order,
                    configuration,
                    ShippingFulfillmentState.OutcomeUnknown,
                    attempts,
                    bookingReference,
                    null,
                    null,
                    exception,
                    isEventHandler).ConfigureAwait(false);
                throw;
            }
            catch (Exception exception)
            {
                return await SaveFailureAsync(
                    order,
                    configuration,
                    ShippingFulfillmentState.Failed,
                    attempts,
                    bookingReference,
                    null,
                    null,
                    exception,
                    isEventHandler).ConfigureAwait(false);
            }

            try
            {
                order = await SaveStateAsync(
                    order,
                    configuration,
                    ShippingFulfillmentState.Created,
                    attempts,
                    result.BookingReference ?? bookingReference,
                    result.ShipmentId,
                    result.TrackingNumber,
                    null,
                    isEventHandler,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                return await SaveFailureAsync(
                    order,
                    configuration,
                    ShippingFulfillmentState.OutcomeUnknown,
                    attempts,
                    result.BookingReference ?? bookingReference,
                    result.ShipmentId,
                    result.TrackingNumber,
                    exception,
                    isEventHandler).ConfigureAwait(false);
            }

            try
            {
                await _activityLog.AddOrderLogAsync(
                    order.UniqueId,
                    $"Shipment created with {configuration.CarrierAlias}.",
                    logType: OrderActivityLogType.Success,
                    ct: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Could not add shipment activity log for order {OrderId}", order.UniqueId);
            }

            return ReadRecord(order)!;
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task<ShippingFulfillmentRecord> SaveFailureAsync(
        IOrderInfo order,
        EkomShippingMethodConfiguration configuration,
        ShippingFulfillmentState state,
        int attempts,
        string? bookingReference,
        string? shipmentId,
        string? trackingNumber,
        Exception exception,
        bool isEventHandler)
    {
        _logger.LogError(
            exception,
            "Shipment creation for order {OrderId} entered state {ShipmentState}",
            order.UniqueId,
            state);
        var message = SafeError(exception);
        var updated = await SaveStateAsync(
            order,
            configuration,
            state,
            attempts,
            bookingReference,
            shipmentId,
            trackingNumber,
            message,
            isEventHandler,
            CancellationToken.None).ConfigureAwait(false);
        await _activityLog.AddOrderLogAsync(
            order.UniqueId,
            $"Shipment creation entered state {state}: {message}",
            logType: OrderActivityLogType.Alert,
            ct: CancellationToken.None).ConfigureAwait(false);
        return ReadRecord(updated)!;
    }

    private async Task<IOrderInfo> SaveStateAsync(
        IOrderInfo order,
        EkomShippingMethodConfiguration configuration,
        ShippingFulfillmentState state,
        int attempts,
        string? bookingReference,
        string? shipmentId,
        string? trackingNumber,
        string? error,
        bool isEventHandler,
        CancellationToken cancellationToken)
    {
        var data = order.ShippingProvider.CustomData.ToDictionary(
            x => x.Key,
            x => WebUtility.HtmlDecode(x.Value),
            StringComparer.OrdinalIgnoreCase);
        SetOrRemove(data, EkomShippingPropertyAliases.CustomShipmentReference, bookingReference);
        SetOrRemove(data, EkomShippingPropertyAliases.CustomShipmentId, shipmentId);
        SetOrRemove(data, EkomShippingPropertyAliases.CustomTrackingNumber, trackingNumber);
        SetOrRemove(data, EkomShippingPropertyAliases.CustomShipmentLastError, error);
        data[EkomShippingPropertyAliases.CustomShipmentState] = state.ToString();
        data[EkomShippingPropertyAliases.CustomShipmentAttempts] = attempts.ToString(CultureInfo.InvariantCulture);
        data[EkomShippingPropertyAliases.CustomShipmentUpdatedUtc] = DateTime.UtcNow.ToString("O");
        data[EkomShippingPropertyAliases.CustomCarrierAlias] = configuration.CarrierAlias;
        data[EkomShippingPropertyAliases.CustomAccountReference] = configuration.AccountReference;
        data[EkomShippingPropertyAliases.CustomServiceId] = configuration.ServiceId;

        return await _orderApi.UpdateShippingInformationAsync(
            order.ShippingProvider.Key,
            order.StoreInfo.Alias,
            data,
            new OrderSettings
            {
                OrderInfo = order,
                IsEventHandler = false,
                FireEvents = false,
                OrderDynamicRequest = new OrderDynamicRequest
                {
                    Title = order.ShippingProvider.Title,
                    Prices = order.ShippingProvider.Prices,
                },
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<IOrderInfo> GetRequiredOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
        await _orderApi.GetOrderAsync(orderId, cancellationToken).ConfigureAwait(false)
        ?? throw new ShippingException($"Order '{orderId}' was not found.");

    private static ShippingFulfillmentRecord? ReadRecord(IOrderInfo order)
    {
        var shippingProvider = order.ShippingProvider;
        if (shippingProvider is null)
        {
            return null;
        }

        var data = shippingProvider.CustomData;
        if (!TryGet(data, EkomShippingPropertyAliases.CustomShipmentState, out var stateValue) ||
            !Enum.TryParse<ShippingFulfillmentState>(WebUtility.HtmlDecode(stateValue), true, out var state))
        {
            if (!TryGet(data, "customshippingDroppOrderId", out var legacyId) ||
                string.IsNullOrWhiteSpace(legacyId))
            {
                return null;
            }

            state = ShippingFulfillmentState.Created;
        }

        var carrierAlias = Get(data, EkomShippingPropertyAliases.CustomCarrierAlias);
        var accountReference = Get(data, EkomShippingPropertyAliases.CustomAccountReference);
        var serviceId = Get(data, EkomShippingPropertyAliases.CustomServiceId);
        var configuration = !string.IsNullOrWhiteSpace(carrierAlias) &&
                            !string.IsNullOrWhiteSpace(accountReference) &&
                            !string.IsNullOrWhiteSpace(serviceId)
            ? new EkomShippingMethodConfiguration(carrierAlias, accountReference, serviceId)
            : EkomShippingMethodConfiguration.FromProperties(
                shippingProvider.Key,
                shippingProvider.Properties);
        if (configuration is null)
        {
            return null;
        }

        _ = int.TryParse(Get(data, EkomShippingPropertyAliases.CustomShipmentAttempts), out var attempts);
        _ = DateTime.TryParse(
            Get(data, EkomShippingPropertyAliases.CustomShipmentUpdatedUtc),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var updatedUtc);
        var shipmentId = Get(data, EkomShippingPropertyAliases.CustomShipmentId, "customshippingDroppOrderId");
        var reference = Get(data, EkomShippingPropertyAliases.CustomShipmentReference, "customshippingDroppBarcode");

        return new ShippingFulfillmentRecord(
            order.UniqueId,
            shippingProvider.Key,
            configuration.CarrierAlias,
            configuration.AccountReference,
            configuration.ServiceId,
            state,
            reference,
            shipmentId,
            Get(data, EkomShippingPropertyAliases.CustomTrackingNumber),
            attempts,
            Get(data, EkomShippingPropertyAliases.CustomShipmentLastError),
            order.CreateDate.ToUniversalTime(),
            updatedUtc == default ? order.UpdateDate.ToUniversalTime() : updatedUtc.ToUniversalTime());
    }

    private static void SetOrRemove(Dictionary<string, string> data, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            data.Remove(key);
        }
        else
        {
            data[key] = value;
        }
    }

    private static bool TryGet(
        IReadOnlyDictionary<string, string>? data,
        string key,
        out string value)
    {
        if (data is not null && data.TryGetValue(key, out var found) && found is not null)
        {
            value = found;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static string? Get(IReadOnlyDictionary<string, string> data, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (data.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return WebUtility.HtmlDecode(value);
            }
        }

        return null;
    }

    private static string SafeError(Exception exception)
    {
        var message = exception switch
        {
            InvalidShippingSelectionException => exception.Message,
            ShippingConfigurationException => exception.Message,
            ShippingProviderException => exception.Message,
            ShippingException => exception.Message,
            _ => "Unexpected shipment processing error.",
        };
        return message.Length <= 500 ? message : message[..500];
    }
}
