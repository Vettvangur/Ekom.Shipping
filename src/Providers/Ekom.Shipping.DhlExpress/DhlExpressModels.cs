using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ekom.Shipping.DhlExpress;

public sealed record DhlExpressAccount(string TypeCode, string Number);

public sealed record DhlExpressAddress(
    string PostalCode,
    string CityName,
    string CountryCode,
    string AddressLine1,
    string? ProvinceCode = null,
    string? AddressLine2 = null,
    string? AddressLine3 = null,
    string? CountyName = null,
    string? ProvinceName = null,
    string? CountryName = null);

public sealed record DhlExpressContact(
    string Phone,
    string CompanyName,
    string FullName,
    string? Email = null,
    string? MobilePhone = null);

public sealed record DhlExpressRegistrationNumber(
    string TypeCode,
    string Number,
    string IssuerCountryCode);

public sealed record DhlExpressParty(
    DhlExpressAddress PostalAddress,
    DhlExpressContact ContactInformation,
    IReadOnlyList<DhlExpressRegistrationNumber>? RegistrationNumbers = null,
    string? TypeCode = null);

public sealed record DhlExpressDimensions(decimal Length, decimal Width, decimal Height);

public sealed record DhlExpressPackage(
    decimal Weight,
    DhlExpressDimensions? Dimensions = null,
    string? TypeCode = null,
    IReadOnlyList<DhlExpressReference>? CustomerReferences = null,
    string? Description = null,
    string? LabelDescription = null,
    int? ReferenceNumber = null);

public sealed record DhlExpressRatingPackage(
    decimal Weight,
    DhlExpressDimensions? Dimensions = null,
    string? TypeCode = null);

public sealed record DhlExpressReference(string Value, string? TypeCode = null);
public sealed record DhlExpressService(string ServiceCode, decimal? Value = null, string? Currency = null, string? Method = null);
public sealed record DhlExpressMonetaryAmount(string TypeCode, decimal Value, string Currency);

public sealed record DhlExpressRatingAddress(
    string PostalCode,
    string CityName,
    string CountryCode,
    string? ProvinceCode = null,
    string? AddressLine1 = null,
    string? AddressLine2 = null,
    string? AddressLine3 = null,
    string? CountyName = null);

public sealed record DhlExpressRateCustomerDetails(
    DhlExpressRatingAddress ShipperDetails,
    DhlExpressRatingAddress ReceiverDetails);

public sealed record DhlExpressRateRequest(
    DhlExpressRateCustomerDetails CustomerDetails,
    string PlannedShippingDateAndTime,
    string UnitOfMeasurement,
    bool IsCustomsDeclarable,
    IReadOnlyList<DhlExpressRatingPackage> Packages,
    IReadOnlyList<DhlExpressAccount>? Accounts = null,
    string? ProductCode = null,
    IReadOnlyList<DhlExpressService>? ValueAddedServices = null,
    IReadOnlyList<DhlExpressMonetaryAmount>? MonetaryAmount = null,
    bool? ReturnStandardProductsOnly = null,
    bool? NextBusinessDay = null);

public sealed record DhlExpressOnePieceRequest(
    string AccountNumber,
    string OriginCountryCode,
    string OriginCityName,
    string DestinationCountryCode,
    string DestinationCityName,
    decimal Weight,
    decimal Length,
    decimal Width,
    decimal Height,
    string PlannedShippingDate,
    bool IsCustomsDeclarable,
    string UnitOfMeasurement,
    string? OriginPostalCode = null,
    string? DestinationPostalCode = null,
    bool? NextBusinessDay = null,
    bool? StrictValidation = null,
    bool? GetAllValueAddedServices = null,
    bool? RequestEstimatedDeliveryDate = null,
    string? EstimatedDeliveryDateType = null);

public sealed record DhlExpressAddressValidationRequest(
    string Type,
    string CountryCode,
    string? PostalCode = null,
    string? CityName = null,
    string? CountyName = null,
    bool? StrictValidation = null);

public sealed record DhlExpressShipmentCustomers(
    DhlExpressParty ShipperDetails,
    DhlExpressParty ReceiverDetails,
    DhlExpressParty? BuyerDetails = null,
    DhlExpressParty? ImporterDetails = null,
    DhlExpressParty? ExporterDetails = null,
    DhlExpressParty? SellerDetails = null,
    DhlExpressParty? PayerDetails = null,
    DhlExpressParty? ManufacturerDetails = null,
    DhlExpressParty? UltimateConsigneeDetails = null,
    DhlExpressParty? BrokerDetails = null);

public sealed record DhlExpressPickupInstruction(
    bool IsRequested,
    string? CloseTime = null,
    string? Location = null,
    IReadOnlyList<DhlExpressInstruction>? SpecialInstructions = null,
    DhlExpressParty? PickupDetails = null,
    DhlExpressParty? PickupRequestorDetails = null);

public sealed record DhlExpressInstruction(string Value, string? TypeCode = null);

public sealed record DhlExpressQuantity(decimal Value, string UnitOfMeasurement);
public sealed record DhlExpressWeight(decimal? NetValue = null, decimal? GrossValue = null);
public sealed record DhlExpressCommodityCode(string TypeCode, string Value);

public sealed record DhlExpressExportLineItem(
    int Number,
    string Description,
    decimal Price,
    DhlExpressQuantity Quantity,
    string ManufacturerCountry,
    DhlExpressWeight Weight,
    IReadOnlyList<DhlExpressCommodityCode>? CommodityCodes = null,
    string? ExportReasonType = null,
    bool? IsTaxesPaid = null,
    IReadOnlyList<string>? AdditionalInformation = null,
    decimal? PreCalculatedLineItemTotalValue = null);

public sealed record DhlExpressInvoice(
    string Number,
    string Date,
    string Function,
    string? SignatureName = null,
    string? SignatureTitle = null,
    IReadOnlyList<string>? Instructions = null,
    decimal? TotalNetWeight = null,
    decimal? TotalGrossWeight = null,
    string? TermsOfPayment = null);

public sealed record DhlExpressExportDeclaration(
    IReadOnlyList<DhlExpressExportLineItem> LineItems,
    DhlExpressInvoice? Invoice = null,
    IReadOnlyList<DhlExpressRemark>? Remarks = null,
    IReadOnlyList<DhlExpressAdditionalCharge>? AdditionalCharges = null,
    string? DestinationPortName = null,
    string? PlaceOfIncoterm = null,
    string? PayerVATNumber = null,
    string? RecipientReference = null,
    string? ExportReference = null,
    string? ExportReason = null,
    string? ExportReasonType = null,
    string? ShipmentType = null);

public sealed record DhlExpressRemark(string Value);
public sealed record DhlExpressAdditionalCharge(decimal Value, string TypeCode);

public sealed record DhlExpressShipmentContent(
    IReadOnlyList<DhlExpressPackage> Packages,
    bool IsCustomsDeclarable,
    string Description,
    string Incoterm,
    string UnitOfMeasurement,
    decimal? DeclaredValue = null,
    string? DeclaredValueCurrency = null,
    DhlExpressExportDeclaration? ExportDeclaration = null,
    bool? AreMorePackagesToBeAddedLater = null,
    [property: JsonPropertyName("USFilingTypeValue")] string? UsFilingTypeValue = null);

public sealed record DhlExpressImageOption(
    string TypeCode,
    string? TemplateName = null,
    bool? IsRequested = null,
    bool? HideAccountNumber = null,
    int? NumberOfCopies = null,
    string? InvoiceType = null,
    string? LanguageCode = null,
    string? LanguageCountryCode = null,
    bool? FitLabelsToA4 = null);

public sealed record DhlExpressOutputImageProperties(
    int? PrinterDPI = null,
    string? EncodingFormat = null,
    IReadOnlyList<DhlExpressImageOption>? ImageOptions = null,
    bool? SplitTransportAndWaybillDocLabels = null,
    bool? AllDocumentsInOneImage = null,
    bool? SplitDocumentsByPages = null);

public sealed record DhlExpressShipmentRequest(
    string PlannedShippingDateAndTime,
    DhlExpressPickupInstruction Pickup,
    string ProductCode,
    IReadOnlyList<DhlExpressAccount> Accounts,
    DhlExpressShipmentCustomers CustomerDetails,
    DhlExpressShipmentContent Content,
    string? LocalProductCode = null,
    bool? GetRateEstimates = null,
    IReadOnlyList<DhlExpressService>? ValueAddedServices = null,
    DhlExpressOutputImageProperties? OutputImageProperties = null,
    IReadOnlyList<DhlExpressReference>? CustomerReferences = null,
    bool? RequestOndemandDeliveryURL = null);

public sealed class DhlExpressShipmentResponse
{
    public string? Url { get; init; }
    public string? ShipmentTrackingNumber { get; init; }
    public string? CancelPickupUrl { get; init; }
    public string? TrackingUrl { get; init; }
    public string? DispatchConfirmationNumber { get; init; }
    public IReadOnlyList<DhlExpressShipmentPackageResponse> Packages { get; init; } = [];
    public IReadOnlyList<DhlExpressDocument> Documents { get; init; } = [];
    public string? OnDemandDeliveryURL { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; init; }
}

public sealed record DhlExpressShipmentPackageResponse(
    string TrackingNumber,
    int? ReferenceNumber = null,
    string? TrackingUrl = null,
    decimal? VolumetricWeight = null,
    IReadOnlyList<DhlExpressDocument>? Documents = null);

public sealed record DhlExpressDocument(
    string ImageFormat,
    string Content,
    string TypeCode,
    int? PackageReferenceNumber = null);

public sealed class DhlExpressRatesResponse
{
    public IReadOnlyList<DhlExpressRatedProduct> Products { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed class DhlExpressProductsResponse
{
    public IReadOnlyList<DhlExpressProduct> Products { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public class DhlExpressProduct
{
    public string? ProductName { get; init; }
    public string? ProductCode { get; init; }
    public string? LocalProductCode { get; init; }
    public string? LocalProductCountryCode { get; init; }
    public string? NetworkTypeCode { get; init; }
    public bool? IsCustomerAgreement { get; init; }
    public DhlExpressCapability? PickupCapabilities { get; init; }
    public DhlExpressCapability? DeliveryCapabilities { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; init; }
}

public sealed class DhlExpressRatedProduct : DhlExpressProduct
{
    public IReadOnlyList<DhlExpressPrice> TotalPrice { get; init; } = [];
    public string? PricingDate { get; init; }
}

public sealed record DhlExpressPrice(string? CurrencyType, string? PriceCurrency, decimal Price);

public sealed class DhlExpressCapability
{
    public string? EstimatedDeliveryDateAndTime { get; init; }
    public string? LocalCutoffDateAndTime { get; init; }
    public string? PickupEarliest { get; init; }
    public string? PickupLatest { get; init; }
    public string? OriginServiceAreaCode { get; init; }
    public string? DestinationServiceAreaCode { get; init; }
    public decimal? TotalTransitDays { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; init; }
}

public sealed class DhlExpressAddressValidationResponse
{
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<DhlExpressValidatedAddress> Address { get; init; } = [];
}

public sealed record DhlExpressValidatedAddress(
    string CountryCode,
    string PostalCode,
    string? CityName = null,
    string? CountyName = null,
    DhlExpressServiceArea? ServiceArea = null);

public sealed record DhlExpressServiceArea(
    string? Code = null,
    string? Description = null,
    [property: JsonPropertyName("GMTOffset")] string? GmtOffset = null);

public sealed class DhlExpressTrackingResponse
{
    public IReadOnlyList<DhlExpressTrackedShipment> Shipments { get; init; } = [];
}

public sealed class DhlExpressTrackedShipment
{
    public string? ShipmentTrackingNumber { get; init; }
    public string? Status { get; init; }
    public string? ShipmentTimestamp { get; init; }
    public string? ProductCode { get; init; }
    public string? Description { get; init; }
    public decimal? TotalWeight { get; init; }
    public string? UnitOfMeasurements { get; init; }
    public IReadOnlyList<DhlExpressTrackingEvent> Events { get; init; } = [];
    public IReadOnlyList<DhlExpressTrackedPiece> Pieces { get; init; } = [];
    public string? EstimatedDeliveryDate { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; init; }
}

public sealed class DhlExpressTrackingEvent
{
    public string? Date { get; init; }
    public string? Time { get; init; }
    [JsonPropertyName("GMTOffset")]
    public string? GmtOffset { get; init; }
    public string? TypeCode { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<DhlExpressServiceArea> ServiceArea { get; init; } = [];
    public IReadOnlyList<DhlExpressTrackingRemark> Remarks { get; init; } = [];
    public string? SignedBy { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; init; }
}

public sealed record DhlExpressTrackingRemark(string? Value = null, string? Details = null);

public sealed class DhlExpressTrackedPiece
{
    public int? Number { get; init; }
    public string? TrackingNumber { get; init; }
    public decimal? Weight { get; init; }
    public decimal? DimensionalWeight { get; init; }
    public decimal? ActualWeight { get; init; }
    public DhlExpressDimensions? Dimensions { get; init; }
    public DhlExpressDimensions? ActualDimensions { get; init; }
    public IReadOnlyList<DhlExpressTrackingEvent> Events { get; init; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; init; }
}

public sealed class DhlExpressDocumentResponse
{
    public IReadOnlyList<DhlExpressEncodedDocument> Documents { get; init; } = [];
}

public sealed record DhlExpressEncodedDocument(
    string? EncodingFormat = null,
    string? Content = null,
    string? TypeCode = null);

public sealed record DhlExpressImageRequest(
    string TypeCode,
    string PickupYearAndMonth,
    string? ShipperAccountNumber = null,
    string? PayerAccountNumber = null,
    string? EncodingFormat = null,
    bool? AllInOnePDF = null,
    bool? CompressedPackage = null);

public sealed class DhlExpressDocumentImageResponse
{
    public IReadOnlyList<DhlExpressRetrievedDocument> Documents { get; init; } = [];
}

public sealed record DhlExpressRetrievedDocument(
    string ShipmentTrackingNumber,
    string TypeCode,
    string EncodingFormat,
    string Content,
    string? Function = null);

public sealed record DhlExpressUploadDocument(
    string Content,
    string? TypeCode = null,
    string? ImageFormat = null);

public sealed record DhlExpressImageUploadRequest(
    string OriginalPlannedShippingDate,
    IReadOnlyList<DhlExpressAccount> Accounts,
    string ProductCode,
    IReadOnlyList<DhlExpressUploadDocument> DocumentImages);

public sealed record DhlExpressInvoiceDataContent(
    IReadOnlyList<DhlExpressUploadExportDeclaration> ExportDeclaration,
    string Currency,
    string UnitOfMeasurement);

public sealed record DhlExpressUploadExportDeclaration(
    IReadOnlyList<DhlExpressExportLineItem> LineItems,
    DhlExpressUploadInvoice Invoice,
    string Incoterm,
    string? ExportReasonType = null,
    string? ShipmentType = null);

public sealed record DhlExpressUploadInvoice(string Number, string Date, string Function);

public sealed record DhlExpressInvoiceDataRequest(
    DhlExpressInvoiceDataContent Content,
    string? PlannedShipDate = null,
    IReadOnlyList<DhlExpressAccount>? Accounts = null,
    DhlExpressInvoiceCustomers? CustomerDetails = null);

public sealed record DhlExpressInvoiceCustomers(
    DhlExpressParty? SellerDetails = null,
    DhlExpressParty? BuyerDetails = null,
    DhlExpressParty? ImporterDetails = null,
    DhlExpressParty? ExporterDetails = null,
    DhlExpressParty? ManufacturerDetails = null,
    DhlExpressParty? UltimateConsigneeDetails = null,
    DhlExpressParty? BrokerDetails = null);

public sealed record DhlExpressPickupShipment(
    string ProductCode,
    bool IsCustomsDeclarable,
    string UnitOfMeasurement,
    IReadOnlyList<DhlExpressRatingPackage> Packages,
    IReadOnlyList<DhlExpressAccount>? Accounts = null,
    IReadOnlyList<DhlExpressService>? ValueAddedServices = null,
    decimal? DeclaredValue = null,
    string? DeclaredValueCurrency = null,
    string? ShipmentTrackingNumber = null);

public sealed record DhlExpressPickupCustomers(
    DhlExpressPickupParty ShipperDetails,
    DhlExpressPickupParty? ReceiverDetails = null,
    DhlExpressPickupBookingRequestor? BookingRequestorDetails = null,
    DhlExpressPickupParty? PickupDetails = null);

public sealed record DhlExpressPickupParty(
    DhlExpressPickupAddress PostalAddress,
    DhlExpressContact ContactInformation);

public sealed record DhlExpressPickupBookingRequestor(
    DhlExpressContact ContactInformation,
    DhlExpressPickupAddress? PostalAddress = null);

public sealed record DhlExpressPickupAddress(
    string PostalCode,
    string CityName,
    string CountryCode,
    string AddressLine1,
    string? ProvinceCode = null,
    string? AddressLine2 = null,
    string? AddressLine3 = null,
    string? CountyName = null);

public sealed record DhlExpressPickupRequest(
    string PlannedPickupDateAndTime,
    IReadOnlyList<DhlExpressAccount> Accounts,
    DhlExpressPickupCustomers CustomerDetails,
    IReadOnlyList<DhlExpressPickupShipment> ShipmentDetails,
    string? CloseTime = null,
    string? Location = null,
    string? LocationType = null,
    IReadOnlyList<DhlExpressInstruction>? SpecialInstructions = null,
    string? Remark = null);

public sealed class DhlExpressPickupResponse
{
    public IReadOnlyList<string> DispatchConfirmationNumbers { get; init; } = [];
    public string? ReadyByTime { get; init; }
    public string? NextPickupDate { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record DhlExpressPickupUpdateRequest(
    string DispatchConfirmationNumber,
    string OriginalShipperAccountNumber,
    string PlannedPickupDateAndTime,
    IReadOnlyList<DhlExpressAccount> Accounts,
    DhlExpressPickupCustomers CustomerDetails,
    IReadOnlyList<DhlExpressPickupShipment>? ShipmentDetails = null,
    string? CloseTime = null,
    string? Location = null,
    string? LocationType = null,
    IReadOnlyList<DhlExpressInstruction>? SpecialInstructions = null,
    string? Remark = null);

public sealed class DhlExpressPickupUpdateResponse
{
    public string? DispatchConfirmationNumber { get; init; }
    public string? ReadyByTime { get; init; }
    public string? NextPickupDate { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed class DhlExpressError
{
    public string? Instance { get; init; }
    public string? Detail { get; init; }
    public string? Title { get; init; }
    public string? Message { get; init; }
    public IReadOnlyList<string> AdditionalDetails { get; init; } = [];
    public string? Status { get; init; }
}
