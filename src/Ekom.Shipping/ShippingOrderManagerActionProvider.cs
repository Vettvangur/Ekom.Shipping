using Ekom.Models;
using Ekom.Models.Manager;
using Ekom.Services;

namespace Ekom.Shipping.Ekom;

internal sealed class ShippingOrderManagerActionProvider : IOrderManagerActionProvider
{
    internal const string CreateAction = "ekom-shipping-create";
    internal const string RetryAction = "ekom-shipping-retry";
    internal const string LabelAction = "ekom-shipping-label";
    private readonly IShippingFulfillmentService _fulfillment;
    private readonly IShippingFulfillmentCarrierRegistry _carriers;

    public ShippingOrderManagerActionProvider(
        IShippingFulfillmentService fulfillment,
        IShippingFulfillmentCarrierRegistry carriers)
    {
        _fulfillment = fulfillment;
        _carriers = carriers;
    }

    public async Task<IReadOnlyCollection<OrderManagerAction>> GetActionsAsync(
        IOrderInfo orderInfo,
        CancellationToken ct = default)
    {
        var record = await _fulfillment.GetAsync(orderInfo.UniqueId, ct).ConfigureAwait(false);
        var configuration = EkomShippingMethodConfiguration.FromProperties(
                orderInfo.ShippingProvider.Key,
                orderInfo.ShippingProvider.Properties);
        var carrierAlias = record?.CarrierAlias ?? configuration?.CarrierAlias;
        if (string.IsNullOrWhiteSpace(carrierAlias) || !_carriers.Carriers.Any(x =>
                string.Equals(x.Alias, carrierAlias, StringComparison.OrdinalIgnoreCase)))
        {
            return Array.Empty<OrderManagerAction>();
        }

        return record?.State switch
        {
            ShippingFulfillmentState.Created =>
            [
                new OrderManagerAction
                {
                    Key = LabelAction,
                    Label = "Print shipping label",
                    Look = "primary",
                    SortOrder = 20,
                },
            ],
            ShippingFulfillmentState.Failed =>
            [
                new OrderManagerAction
                {
                    Key = RetryAction,
                    Label = "Retry shipment",
                    Look = "warning",
                    ConfirmMessage = "Retry creating this carrier shipment?",
                    SortOrder = 20,
                },
            ],
            ShippingFulfillmentState.OutcomeUnknown =>
            [
                new OrderManagerAction
                {
                    Key = RetryAction,
                    Label = "Shipment outcome unknown",
                    Look = "danger",
                    Enabled = false,
                    SortOrder = 20,
                },
            ],
            ShippingFulfillmentState.Processing or ShippingFulfillmentState.Submitting =>
            [
                new OrderManagerAction
                {
                    Key = CreateAction,
                    Label = "Creating shipment…",
                    Enabled = false,
                    SortOrder = 20,
                },
            ],
            _ =>
            [
                new OrderManagerAction
                {
                    Key = CreateAction,
                    Label = "Create shipment",
                    Look = "primary",
                    ConfirmMessage = "Create this carrier shipment?",
                    SortOrder = 20,
                },
            ],
        };
    }

    public async Task<OrderManagerActionExecutionResult?> ExecuteAsync(
        IOrderInfo orderInfo,
        string actionKey,
        string? userName = null,
        CancellationToken ct = default)
    {
        try
        {
            switch (actionKey)
            {
                case CreateAction:
                    var created = await _fulfillment.CreateAsync(orderInfo.UniqueId, ct).ConfigureAwait(false);
                    return ResultFor(created);
                case RetryAction:
                    var retried = await _fulfillment.RetryAsync(orderInfo.UniqueId, ct).ConfigureAwait(false);
                    return ResultFor(retried);
                case LabelAction:
                    var label = await _fulfillment.GetLabelAsync(orderInfo.UniqueId, ct).ConfigureAwait(false);
                    return new OrderManagerActionFileResult
                    {
                        Content = label.Content,
                        ContentType = label.ContentType,
                        FileName = label.FileName,
                    };
                default:
                    return null;
            }
        }
        catch (Exception exception) when (exception is ShippingException or ArgumentException)
        {
            return new OrderManagerActionBadRequestResult
            {
                Message = exception.Message,
            };
        }
    }

    private static OrderManagerActionExecutionResult ResultFor(ShippingFulfillmentRecord record) =>
        record.State == ShippingFulfillmentState.Created
            ? new OrderManagerActionSuccessResult { Message = "Shipment created." }
            : new OrderManagerActionBadRequestResult
            {
                Message = record.LastError ?? $"Shipment state: {record.State}.",
            };
}
