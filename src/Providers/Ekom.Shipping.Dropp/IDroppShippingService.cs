namespace Ekom.Shipping.Dropp;

public interface IDroppShippingService :
    IShippingCarrier,
    IShippingFulfillmentCarrier,
    IShippingShipmentLookupCarrier,
    IShippingShipmentDeletionCarrier
{
    Task<DroppOrder> CreateOrderAsync(
        string accountReference,
        DroppCreateOrderRequest request,
        CancellationToken cancellationToken = default);

    Task<DroppOrder> GetOrderAsync(
        string accountReference,
        Guid orderId,
        CancellationToken cancellationToken = default);

    Task UpdateOrderAsync(
        string accountReference,
        Guid orderId,
        DroppUpdateOrderRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteOrderAsync(
        string accountReference,
        Guid orderId,
        CancellationToken cancellationToken = default);

    Task<DroppDeliveryPostalCodes> GetDeliveryPostalCodesAsync(
        string accountReference,
        CancellationToken cancellationToken = default);

    Task<ShippingLabel> GetReturnLabelAsync(
        string accountReference,
        string barcode,
        CancellationToken cancellationToken = default);

    Task<ShippingLabel> CreateExtraPackageLabelAsync(
        string accountReference,
        Guid orderId,
        CancellationToken cancellationToken = default);

    Task DeleteExtraOrderAsync(
        string accountReference,
        Guid orderId,
        string barcode,
        CancellationToken cancellationToken = default);
}
