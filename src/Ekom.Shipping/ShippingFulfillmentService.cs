using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    private readonly IReadOnlyList<IShippingDocumentStore> _documentStores;
    private readonly IOrderActivityLogService _activityLog;
    private readonly ILogger<ShippingFulfillmentService> _logger;

    public ShippingFulfillmentService(
        global::Ekom.API.Order orderApi,
        IShippingFulfillmentCarrierRegistry carriers,
        IShippingOrderMapper mapper,
        IEnumerable<IShippingAutomationRule> automationRules,
        IEnumerable<IShippingDocumentStore> documentStores,
        IOrderActivityLogService activityLog,
        ILogger<ShippingFulfillmentService> logger)
    {
        _orderApi = orderApi;
        _carriers = carriers;
        _mapper = mapper;
        _automationRules = automationRules;
        _documentStores = documentStores.ToArray();
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
        if (order is null)
        {
            return null;
        }

        var record = ReadRecord(order);
        if (record?.State != ShippingFulfillmentState.Deleting || !IsStale(record))
        {
            return record;
        }

        var semaphore = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            order = await GetRequiredOrderAsync(orderId, cancellationToken).ConfigureAwait(false);
            record = ReadRecord(order);
            return record?.State == ShippingFulfillmentState.Deleting && IsStale(record)
                ? await MarkDeletionOutcomeUnknownAsync(order, record).ConfigureAwait(false)
                : record;
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task<ShippingLabel> GetLabelAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrderAsync(orderId, cancellationToken).ConfigureAwait(false);
        var record = ReadRecord(order)
            ?? throw new ShippingException("This order does not have a shipment.");
        var storedLabels = record.Documents?.Where(document =>
            string.Equals(document.TypeCode, "label", StringComparison.OrdinalIgnoreCase)).ToArray() ?? [];
        if (storedLabels.Length > 0)
        {
            var documentStore = GetDocumentStore();
            var labels = new List<ShippingLabel>(storedLabels.Length);
            foreach (var storedLabel in storedLabels)
            {
                labels.Add(await documentStore.GetAsync(storedLabel.Reference, cancellationToken).ConfigureAwait(false)
                    ?? throw new ShippingException($"Stored shipping label '{storedLabel.Reference}' could not be found."));
            }

            if (labels.Count == 1)
            {
                return labels[0];
            }

            using var stream = new MemoryStream();
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                for (var index = 0; index < labels.Count; index++)
                {
                    var label = labels[index];
                    var entry = archive.CreateEntry($"{index + 1}-{label.FileName}", CompressionLevel.Fastest);
                    await using var entryStream = entry.Open();
                    await entryStream.WriteAsync(label.Content, cancellationToken).ConfigureAwait(false);
                }
            }

            return new ShippingLabel(stream.ToArray(), "application/zip", "shipping-labels.zip");
        }

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

    public async Task<ShippingShipmentDocument> GetShipmentAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrderAsync(orderId, cancellationToken).ConfigureAwait(false);
        var record = ReadRecord(order)
            ?? throw new ShippingException("This order does not have a shipment.");
        if (string.IsNullOrWhiteSpace(record.ShipmentId))
        {
            throw new ShippingException("This order does not have a carrier shipment ID.");
        }

        var carrier = _carriers.GetRequired(record.CarrierAlias);
        if (carrier is not IShippingShipmentLookupCarrier lookupCarrier)
        {
            throw new ShippingException($"Shipping carrier '{record.CarrierAlias}' does not support shipment lookup.");
        }

        return await lookupCarrier.GetShipmentAsync(
            record.AccountReference,
            record.ShipmentId,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ShippingFulfillmentRecord> DeleteAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var semaphore = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var order = await GetRequiredOrderAsync(orderId, cancellationToken).ConfigureAwait(false);
            var record = ReadRecord(order)
                ?? throw new ShippingException("This order does not have a shipment.");
            if (record.State == ShippingFulfillmentState.Deleting && IsStale(record))
            {
                return await MarkDeletionOutcomeUnknownAsync(order, record).ConfigureAwait(false);
            }

            if (record.State != ShippingFulfillmentState.Created || string.IsNullOrWhiteSpace(record.ShipmentId))
            {
                throw new ShippingException("Only a created shipment can be deleted.");
            }

            var carrier = _carriers.GetRequired(record.CarrierAlias);
            if (carrier is not IShippingShipmentDeletionCarrier deletionCarrier)
            {
                throw new ShippingException($"Shipping carrier '{record.CarrierAlias}' does not support shipment deletion.");
            }

            var configuration = new EkomShippingMethodConfiguration(
                record.CarrierAlias,
                record.AccountReference,
                record.ServiceId);
            order = await SaveStateAsync(
                order,
                configuration,
                ShippingFulfillmentState.Deleting,
                record.AttemptCount,
                record.BookingReference,
                record.ShipmentId,
                record.TrackingNumber,
                null,
                isEventHandler: false,
                cancellationToken).ConfigureAwait(false);

            try
            {
                await deletionCarrier.DeleteShipmentAsync(
                    record.AccountReference,
                    record.ShipmentId,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (ShipmentOutcomeUnknownException exception)
            {
                return await SaveDeletionFailureAsync(
                    order,
                    configuration,
                    record,
                    ShippingFulfillmentState.DeleteOutcomeUnknown,
                    exception).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
            {
                await SaveDeletionFailureAsync(
                    order,
                    configuration,
                    record,
                    ShippingFulfillmentState.DeleteOutcomeUnknown,
                    exception).ConfigureAwait(false);
                throw;
            }
            catch (Exception exception)
            {
                await SaveDeletionFailureAsync(
                    order,
                    configuration,
                    record,
                    ShippingFulfillmentState.Created,
                    exception).ConfigureAwait(false);
                throw;
            }

            try
            {
                order = await SaveStateAsync(
                    order,
                    configuration,
                    ShippingFulfillmentState.Deleted,
                    record.AttemptCount,
                    record.BookingReference,
                    record.ShipmentId,
                    record.TrackingNumber,
                    null,
                    isEventHandler: false,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                return await SaveDeletionFailureAsync(
                    order,
                    configuration,
                    record,
                    ShippingFulfillmentState.DeleteOutcomeUnknown,
                    exception).ConfigureAwait(false);
            }

            await AddActivityLogSafelyAsync(
                order.UniqueId,
                $"Shipment deleted from {record.CarrierAlias}.",
                OrderActivityLogType.Success,
                cancellationToken).ConfigureAwait(false);
            return ReadRecord(order)!;
        }
        finally
        {
            semaphore.Release();
        }
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

            if (existing?.State == ShippingFulfillmentState.Deleting && existing.UpdatedUtc < staleBefore)
            {
                return await MarkDeletionOutcomeUnknownAsync(order, existing).ConfigureAwait(false);
            }

            if (existing?.State is ShippingFulfillmentState.Deleting or
                ShippingFulfillmentState.Deleted or
                ShippingFulfillmentState.DeleteOutcomeUnknown)
            {
                throw new ShippingException(
                    $"Shipment creation cannot continue while its state is {existing.State}.");
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

            IShippingDocumentStore? documentStore = null;
            if (carrier is IShippingCarrierRequiresDocumentStore)
            {
                try
                {
                    documentStore = GetDocumentStore();
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
            }

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

            IReadOnlyList<StoredShippingDocument> storedDocuments = [];
            if (result.Documents is { Count: > 0 })
            {
                var storageItems = result.Documents.Select((document, index) =>
                {
                    var stored = new StoredShippingDocument(
                        DocumentReference(order.UniqueId, configuration.CarrierAlias, result.ShipmentId, document, index),
                        document.TypeCode,
                        document.Format,
                        document.ContentType,
                        document.FileName,
                        document.PackageReferenceNumber);
                    return new ShippingDocumentStorageItem(stored, document.Content);
                }).ToArray();
                storedDocuments = storageItems.Select(item => item.Document).ToArray();
                try
                {
                    documentStore ??= GetDocumentStore();
                    order = await SaveStateAsync(
                        order,
                        configuration,
                        ShippingFulfillmentState.OutcomeUnknown,
                        attempts,
                        result.BookingReference ?? bookingReference,
                        result.ShipmentId,
                        result.TrackingNumber,
                        "Shipment documents are awaiting durable storage.",
                        isEventHandler,
                        CancellationToken.None,
                        storedDocuments).ConfigureAwait(false);
                    await documentStore.StoreAsync(
                        new StoreShippingDocumentsRequest(
                            order.UniqueId,
                            configuration.CarrierAlias,
                            result.ShipmentId,
                            storageItems),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException exception)
                {
                    await SaveFailureAsync(
                        order,
                        configuration,
                        ShippingFulfillmentState.OutcomeUnknown,
                        attempts,
                        result.BookingReference ?? bookingReference,
                        result.ShipmentId,
                        result.TrackingNumber,
                        exception,
                        isEventHandler,
                        storedDocuments).ConfigureAwait(false);
                    throw;
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
                        isEventHandler,
                        storedDocuments).ConfigureAwait(false);
                }
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
                    cancellationToken,
                    storedDocuments).ConfigureAwait(false);
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
        bool isEventHandler,
        IReadOnlyList<StoredShippingDocument>? documents = null)
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
            CancellationToken.None,
            documents).ConfigureAwait(false);
        await _activityLog.AddOrderLogAsync(
            order.UniqueId,
            $"Shipment creation entered state {state}: {message}",
            logType: OrderActivityLogType.Alert,
            ct: CancellationToken.None).ConfigureAwait(false);
        return ReadRecord(updated)!;
    }

    private async Task<ShippingFulfillmentRecord> SaveDeletionFailureAsync(
        IOrderInfo order,
        EkomShippingMethodConfiguration configuration,
        ShippingFulfillmentRecord record,
        ShippingFulfillmentState state,
        Exception exception)
    {
        _logger.LogError(
            exception,
            "Shipment deletion for order {OrderId} entered state {ShipmentState}",
            order.UniqueId,
            state);
        var message = SafeError(exception);
        var updated = await SaveStateAsync(
            order,
            configuration,
            state,
            record.AttemptCount,
            record.BookingReference,
            record.ShipmentId,
            record.TrackingNumber,
            message,
            isEventHandler: false,
            CancellationToken.None).ConfigureAwait(false);
        await AddActivityLogSafelyAsync(
            order.UniqueId,
            $"Shipment deletion entered state {state}: {message}",
            OrderActivityLogType.Alert,
            CancellationToken.None).ConfigureAwait(false);
        return ReadRecord(updated)!;
    }

    private async Task<ShippingFulfillmentRecord> MarkDeletionOutcomeUnknownAsync(
        IOrderInfo order,
        ShippingFulfillmentRecord record)
    {
        var configuration = new EkomShippingMethodConfiguration(
            record.CarrierAlias,
            record.AccountReference,
            record.ServiceId);
        return await SaveDeletionFailureAsync(
            order,
            configuration,
            record,
            ShippingFulfillmentState.DeleteOutcomeUnknown,
            new ShippingException("Shipment deletion was interrupted and requires reconciliation."))
            .ConfigureAwait(false);
    }

    private static bool IsStale(ShippingFulfillmentRecord record) =>
        record.UpdatedUtc < DateTime.UtcNow.AddMinutes(-10);

    private async Task AddActivityLogSafelyAsync(
        Guid orderId,
        string message,
        OrderActivityLogType logType,
        CancellationToken cancellationToken)
    {
        try
        {
            await _activityLog.AddOrderLogAsync(
                orderId,
                message,
                logType: logType,
                ct: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not add shipment activity log for order {OrderId}", orderId);
        }
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
        CancellationToken cancellationToken,
        IReadOnlyList<StoredShippingDocument>? documents = null)
    {
        var data = order.ShippingProvider.CustomData.ToDictionary(
            x => x.Key,
            x => WebUtility.HtmlDecode(x.Value),
            StringComparer.OrdinalIgnoreCase);
        SetOrRemove(data, EkomShippingPropertyAliases.CustomShipmentReference, bookingReference);
        SetOrRemove(data, EkomShippingPropertyAliases.CustomShipmentId, shipmentId);
        SetOrRemove(data, EkomShippingPropertyAliases.CustomTrackingNumber, trackingNumber);
        SetOrRemove(data, EkomShippingPropertyAliases.CustomShipmentLastError, error);
        if (documents is not null)
        {
            data[EkomShippingPropertyAliases.CustomShipmentDocuments] = JsonSerializer.Serialize(documents);
        }
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
        var documents = ReadDocuments(data);

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
            updatedUtc == default ? order.UpdateDate.ToUniversalTime() : updatedUtc.ToUniversalTime(),
            documents);
    }

    private IShippingDocumentStore GetDocumentStore()
    {
        if (_documentStores.Count != 1)
        {
            throw new ShippingConfigurationException(
                _documentStores.Count == 0
                    ? "A private shipping document store is required."
                    : "Only one private shipping document store can be registered.");
        }

        return _documentStores[0];
    }

    private static IReadOnlyList<StoredShippingDocument> ReadDocuments(IReadOnlyDictionary<string, string> data)
    {
        var value = Get(data, EkomShippingPropertyAliases.CustomShipmentDocuments);
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<StoredShippingDocument[]>(value) ?? [];
        }
        catch (JsonException)
        {
            throw new ShippingException("Stored shipment document metadata is invalid.");
        }
    }

    private static string DocumentReference(
        Guid orderId,
        string carrierAlias,
        string shipmentId,
        ShippingCreationDocument document,
        int index)
    {
        var source = Encoding.UTF8.GetBytes(
            $"{orderId:N}|{carrierAlias}|{shipmentId}|{index}|{document.TypeCode}|{document.PackageReferenceNumber}");
        return $"shipping/{Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant()}";
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
