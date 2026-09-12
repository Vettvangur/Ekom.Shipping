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
    string? TrackingNumber = null,
    IReadOnlyList<ShippingCreationDocument>? Documents = null);

public sealed record ShippingCreationDocument(
    string TypeCode,
    string Format,
    byte[] Content,
    string ContentType,
    string FileName,
    int? PackageReferenceNumber = null);

public sealed record StoredShippingDocument(
    string Reference,
    string TypeCode,
    string Format,
    string ContentType,
    string FileName,
    int? PackageReferenceNumber = null);

public sealed record StoreShippingDocumentsRequest(
    Guid OrderId,
    string CarrierAlias,
    string ShipmentId,
    IReadOnlyList<ShippingDocumentStorageItem> Documents);

public sealed record ShippingDocumentStorageItem(
    StoredShippingDocument Document,
    byte[] Content);

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
