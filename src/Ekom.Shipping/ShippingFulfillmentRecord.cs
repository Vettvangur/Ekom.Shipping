namespace Ekom.Shipping.Ekom;

public sealed record ShippingFulfillmentRecord(
    Guid OrderId,
    Guid ShippingProviderKey,
    string CarrierAlias,
    string AccountReference,
    string ServiceId,
    ShippingFulfillmentState State,
    string? BookingReference,
    string? ShipmentId,
    string? TrackingNumber,
    int AttemptCount,
    string? LastError,
    DateTime OrderCreatedUtc,
    DateTime UpdatedUtc);
