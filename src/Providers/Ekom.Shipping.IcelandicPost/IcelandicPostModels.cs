using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ekom.Shipping.IcelandicPost;

public sealed record IcelandicPostServiceRequest(
    string? CountryCode = null,
    string? Postcode = null,
    decimal? Weight = null,
    int? Height = null,
    int? Length = null,
    int? Width = null,
    int? CartAmount = null);

public sealed class IcelandicPostDeliveryService
{
    public string DeliveryServiceId { get; init; } = string.Empty;
    public string NameLong { get; init; } = string.Empty;
    public string NameShort { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string DescriptionWords { get; init; } = string.Empty;
    public decimal? Weight { get; init; }
    public string WeightUnit { get; init; } = string.Empty;
    public decimal? MaxNumberOfItems { get; init; }
    public string Logo { get; init; } = string.Empty;
    public int? Length { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public string EstimatedDeliveryTime { get; init; } = string.Empty;
    public bool? HasPddpOutbound { get; init; }
    public IcelandicPostServiceRestrictions? Restrictions { get; init; }
    public IcelandicPostPrice? PriceRelated { get; init; }
    public IcelandicPostOptionalServices? OptionalServices { get; init; }
}

public sealed record IcelandicPostPrice(
    string? Currency = null,
    decimal? PriceWeight = null,
    decimal? BruttoPrice = null,
    decimal? NettoPrice = null,
    decimal? VatPrice = null,
    decimal? VatPercent = null);

public sealed record IcelandicPostServiceRestrictions(
    int? MaxNumberOfItems = null,
    int? MaxLength = null,
    int? MaxWidth = null,
    int? MaxHeight = null,
    int? MaxLengthWidthHeight = null,
    int? MaxWeight = null,
    string? MaxWeightUnit = null);

public sealed record IcelandicPostOptionalServices(
    bool? Cod = null,
    bool? ContractAttached = null,
    bool? DeliverToRecipientOnly = null,
    bool? DeliveryAdvice = null,
    bool? Express = null,
    bool? Fragile = null,
    bool? FragileIncluded = null,
    bool? Insurance = null,
    bool? Pallet = null,
    bool? RecipientPaysPostage = null,
    bool? Reference = null,
    bool? ReturnAllowed = null,
    bool? Large = null,
    bool? LargeIncluded = null);

public sealed record IcelandicPostPickupLocation(
    string Id,
    string Name,
    string Address,
    string Postcode,
    string Town,
    double? Latitude = null,
    double? Longitude = null);

public sealed record IcelandicPostRecipient(
    string Name,
    string AddressLine1,
    string Postcode,
    string CountryCode,
    string? Town = null,
    string? Email = null,
    string? MobilePhone = null,
    string? Nin = null,
    string? Attention = null,
    string? AddressLine2 = null,
    bool? SendNotification = null);

public sealed class IcelandicPostShipmentOptions
{
    public string DeliveryServiceId { get; init; } = string.Empty;
    public string? Reference { get; init; }
    public int? NumberOfItems { get; init; }
    public int? NumberOfCertificates { get; init; }
    public int? NumberOfInvoices { get; init; }
    public int? NumberOfLicense { get; init; }
    public int? StorageDays { get; init; }
    public bool? Cod { get; init; }
    public string? CodAmount { get; init; }
    public string? CodCurrency { get; init; }
    public int? CodSlipNumber { get; init; }
    public string? Description { get; init; }
    public bool? DeliveryAdvice { get; init; }
    public bool? ContractAttached { get; init; }
    public bool? Express { get; init; }
    public bool? Fragile { get; init; }
    public bool? HomeDelivery { get; init; }
    public bool? Insurance { get; init; }
    public string? InsuranceAmount { get; init; }
    public string? InsuranceCurrency { get; init; }
    public bool? RecipientPaysPostage { get; init; }
    public bool? DeliverToRecipientOnly { get; init; }
    public bool? Pallet { get; init; }
    public bool? Large { get; init; }
    public string? PostboxSize { get; init; }
    public string? DoNotForward { get; init; }
    public bool? ReturnAllowed { get; init; }
    public int? ReturnAllowedDays { get; init; }
    public long? ReturnAllowedEndDate { get; init; }
    public string? InstructionsForNonDelivery { get; init; }
    public string? ReasonForExport { get; init; }
    public string? TotalWeight { get; init; }
    public string? WeightUnit { get; init; }
    public string? DimensionUnit { get; init; }
    public int? Length { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public bool? SignatureRequired { get; init; }
    public int? SignatureMinAge { get; init; }
    public string? IossNumber { get; init; }
    public string? TotalVolume { get; init; }
    public string? ExternalCodes { get; init; }
    public bool? PddpShipment { get; init; }
    public bool? ExportCustomsDeclaration { get; init; }
    public string? ExternalShipmentId { get; init; }
    public bool? SaveAsUnregisteredShipmentWhenFailed { get; init; }
}

public sealed record IcelandicPostParcel(
    string? LineNumber = null,
    string? ItemId = null,
    string? Weight = null,
    decimal? Height = null,
    decimal? Length = null,
    decimal? Width = null,
    string? Volume = null,
    string? ExternalItemId = null,
    string? InstructionCodes = null);

public sealed record IcelandicPostCustomsContent(
    int LineNumber,
    string DescriptionOfContents,
    string GoodsQuantity,
    string ValueForCustoms,
    string ValueForCustomsCurrency,
    string HsTariffNumber,
    string CountryOfOrigin);

public sealed record IcelandicPostShipmentRequest(
    IcelandicPostRecipient Recipient,
    IcelandicPostShipmentOptions Options,
    IReadOnlyList<IcelandicPostParcel>? Items = null,
    IReadOnlyList<IcelandicPostCustomsContent>? Contents = null,
    IcelandicPostCustoms? Customs = null);

public sealed record IcelandicPostCustoms(
    string? ShippingCost = null,
    string? InsuredValue = null,
    string? ProductsTotalValue = null,
    string? TotalTax = null,
    string? TotalDuty = null,
    string? SurchargeValue = null,
    string? FlcTotalCostNoSurcharge = null,
    string? FlcTotalCost = null,
    string? Currency = null,
    string? DestinationCountryCurrency = null,
    string? TaxDeminimisValue = null,
    string? DutyDeminimisValue = null);

public sealed class IcelandicPostShipment
{
    private IReadOnlyList<IcelandicPostParcel> _items = [];
    private IReadOnlyList<IcelandicPostCustomsContent> _contents = [];
    private IReadOnlyList<IcelandicPostTrackingEvent> _track = [];

    public string ShipmentId { get; init; } = string.Empty;
    public string CustomerId { get; init; } = string.Empty;
    public string RegistrationKey { get; init; } = string.Empty;
    public decimal? StatusDateTime { get; init; }
    public string StatusCode { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public string StatusLocation { get; init; } = string.Empty;
    public string PaymentStatus { get; init; } = string.Empty;
    public string DuesUnpaid { get; init; } = string.Empty;
    public string DuesTotal { get; init; } = string.Empty;
    public string DuesCurrency { get; init; } = string.Empty;
    public bool? DuesUnknown { get; init; }
    public string ErrorText { get; init; } = string.Empty;
    public decimal? OrderCreationDate { get; init; }
    public decimal? PostingDateTime { get; init; }
    public decimal? DeliverDateTime { get; init; }
    public bool? Active { get; init; }
    public bool? Printed { get; init; }
    public IcelandicPostSender? Sender { get; init; }
    public IcelandicPostRecipient? Recipient { get; init; }
    public IcelandicPostShipmentOptionsResponse? Options { get; init; }
    public IReadOnlyList<IcelandicPostParcel> Items
    {
        get => _items;
        init => _items = value ?? [];
    }

    public IReadOnlyList<IcelandicPostCustomsContent> Contents
    {
        get => _contents;
        init => _contents = value ?? [];
    }

    public IReadOnlyList<IcelandicPostTrackingEvent> Track
    {
        get => _track;
        init => _track = value ?? [];
    }
    public string IossNumber { get; init; } = string.Empty;
    public IcelandicPostCustoms? Customs { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; init; }
}

public sealed record IcelandicPostSender(
    string? UserId = null,
    string? StoreId = null,
    string? Name = null,
    string? AddressLine1 = null,
    string? AddressLine2 = null,
    string? Postcode = null,
    string? Town = null,
    string? CountryCode = null,
    string? Email = null,
    string? MobilePhone = null,
    string? Contact = null,
    bool? SendNotification = null);

public sealed class IcelandicPostShipmentOptionsResponse
{
    public string DeliveryServiceId { get; init; } = string.Empty;
    public string DeliveryServiceName { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
    public decimal? NumberOfItems { get; init; }
    public decimal? NumberOfCertificates { get; init; }
    public decimal? NumberOfInvoices { get; init; }
    public decimal? NumberOfLicense { get; init; }
    public decimal? StorageDays { get; init; }
    public bool? Cod { get; init; }
    public string CodAmount { get; init; } = string.Empty;
    public string CodCurrency { get; init; } = string.Empty;
    public decimal? CodSlipNumber { get; init; }
    public bool? DeliverToRecipientOnly { get; init; }
    public bool? DeliveryAdvice { get; init; }
    public bool? ContractAttached { get; init; }
    public string Description { get; init; } = string.Empty;
    public bool? ExportCustomsDeclaration { get; init; }
    public bool? Express { get; init; }
    public bool? Fragile { get; init; }
    public bool? HomeDelivery { get; init; }
    public string InstructionsForNonDelivery { get; init; } = string.Empty;
    public bool? Insurance { get; init; }
    public string InsuranceAmount { get; init; } = string.Empty;
    public string InsuranceCurrency { get; init; } = string.Empty;
    public string MailClass { get; init; } = string.Empty;
    public bool? Pallet { get; init; }
    public bool? RecipientPaysPostage { get; init; }
    public string ReasonForExport { get; init; } = string.Empty;
    public string PostboxSize { get; init; } = string.Empty;
    public string DimensionUnit { get; init; } = string.Empty;
    public int? Length { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public string DoNotForward { get; init; } = string.Empty;
    public bool? ReturnAllowed { get; init; }
    public decimal? ReturnAllowedEndDate { get; init; }
    public decimal? SentFromPostOffice { get; init; }
    public string ExternalShipmentId { get; init; } = string.Empty;
    public string TotalPayWeight { get; init; } = string.Empty;
    public string TotalVolumeWeight { get; init; } = string.Empty;
    public string TotalWeight { get; init; } = string.Empty;
    public string WeightUnit { get; init; } = string.Empty;
    public bool? SignatureRequired { get; init; }
    public int? SignatureMinAge { get; init; }
    public string IossNumber { get; init; } = string.Empty;
    public string TotalVolume { get; init; } = string.Empty;
    public bool? SaveAsUnregisteredShipmentWhenFailed { get; init; }
    public string ExternalCodes { get; init; } = string.Empty;
    public string ExternalReference { get; init; } = string.Empty;
    public bool? PddpShipment { get; init; }
}

public sealed record IcelandicPostTrackingEvent(
    int? LineNumber,
    string? ItemId,
    decimal? EventDateTime,
    string? EventLocation,
    string? EventDescription,
    string? EventCode);

public sealed record IcelandicPostShipmentStatus(
    string? ShipmentId = null,
    string? CustomerId = null,
    decimal? StatusDateTime = null,
    string? StatusCode = null,
    string? StatusText = null,
    string? StatusLocation = null,
    string? BoxMachineName = null,
    string? LocationName = null);

public sealed record IcelandicPostShipmentReference(
    string? ShipmentId = null,
    string? Reference = null,
    string? ShopId = null,
    bool? IsDelivered = null,
    bool? IsReturnId = null,
    decimal? RegisteredDate = null,
    decimal? PostingDateTime = null,
    [property: JsonPropertyName("deliverDateTIme")] decimal? DeliverDateTime = null);

public sealed class IcelandicPostShipmentSummary
{
    public string ShipmentId { get; init; } = string.Empty;
    public string CustomerId { get; init; } = string.Empty;
    public string RegistrationKey { get; init; } = string.Empty;
    public string StoreId { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public decimal? NumberOfItems { get; init; }
    public decimal? StatusDateTime { get; init; }
    public string StatusCode { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public string StatusLocation { get; init; } = string.Empty;
    public string PaymentStatus { get; init; } = string.Empty;
    public string ErrorText { get; init; } = string.Empty;
    public string DeliveryServiceId { get; init; } = string.Empty;
    public string DeliveryServiceName { get; init; } = string.Empty;
    public decimal? CodAmount { get; init; }
    public string CodCurrency { get; init; } = string.Empty;
    public decimal? OrderCreationDate { get; init; }
    public decimal? PostingDateTime { get; init; }
    public decimal? DeliverDateTime { get; init; }
    public bool? Active { get; init; }
    public bool? Printed { get; init; }
    public IcelandicPostRecipient? Recipient { get; init; }
}

public sealed class IcelandicPostShipmentList
{
    private IReadOnlyList<IcelandicPostShipmentSummary> _shipments = [];

    public string Count { get; init; } = "0";
    public IReadOnlyList<IcelandicPostShipmentSummary> Shipments
    {
        get => _shipments;
        init => _shipments = value ?? [];
    }
}

public sealed record IcelandicPostDocument(byte[] Content, string ContentType, string FileName);

public enum IcelandicPostLabelFormat
{
    A4Size,
    Unknown,
    LabelSize9x15,
    LabelSize10x7,
    LabelSize10x12,
    LabelSize6x10,
    LabelSize9x5,
}
