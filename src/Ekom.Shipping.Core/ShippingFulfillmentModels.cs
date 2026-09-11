namespace Ekom.Shipping;

public sealed record ShipmentRecipient(
    string Name,
    string Email,
    string Phone,
    string Address,
    string PostalCode,
    string City,
    string CountryCode,
    string? NationalId = null);

public sealed record ShipmentItem(string Sku, string Name, int Quantity);

public sealed record ShipmentBookingRequest(
    string MerchantOrderId,
    string ServiceId,
    ShipmentRecipient Recipient,
    IReadOnlyList<ShipmentItem> Items,
    decimal Value,
    string Currency,
    string? PickupLocationId = null,
    string? BookingReference = null);

public sealed record ShipmentBookingResult(
    string ShipmentId,
    string? BookingReference = null,
    string? TrackingNumber = null);

public sealed record ShippingLabel(
    byte[] Content,
    string ContentType,
    string FileName);

public sealed record ShippingTrackingResult(string Content, string ContentType = "application/json");

public sealed record ShippingShipmentDocument(
    byte[] Content,
    string ContentType,
    string FileName,
    string? Status = null);
