namespace Ekom.Shipping.DhlExpress;

public interface IDhlExpressShippingService : IShippingCarrier, IShippingFulfillmentCarrier, IShippingShipmentLookupCarrier
{
    Task<DhlExpressRatesResponse> GetRatesAsync(
        string accountReference,
        DhlExpressRateRequest request,
        bool strictValidation = false,
        CancellationToken cancellationToken = default);

    Task<DhlExpressProductsResponse> GetProductsAsync(
        string accountReference,
        DhlExpressOnePieceRequest request,
        CancellationToken cancellationToken = default);

    Task<DhlExpressAddressValidationResponse> ValidateAddressAsync(
        string accountReference,
        DhlExpressAddressValidationRequest request,
        CancellationToken cancellationToken = default);

    Task<DhlExpressShipmentResponse> CreateShipmentAsync(
        string accountReference,
        DhlExpressShipmentRequest request,
        bool validateDataOnly = false,
        CancellationToken cancellationToken = default);

    Task<DhlExpressTrackingResponse> GetShipmentTrackingAsync(
        string accountReference,
        string shipmentTrackingNumber,
        string trackingView = "all-checkpoints",
        string levelOfDetail = "shipment",
        string language = "eng",
        CancellationToken cancellationToken = default);

    Task<DhlExpressDocumentResponse> GetProofOfDeliveryAsync(
        string accountReference,
        string shipmentTrackingNumber,
        string content = "epod-summary",
        CancellationToken cancellationToken = default);

    Task<DhlExpressDocumentImageResponse> GetImageAsync(
        string accountReference,
        string shipmentTrackingNumber,
        DhlExpressImageRequest request,
        CancellationToken cancellationToken = default);

    Task UploadImagesAsync(
        string accountReference,
        string shipmentTrackingNumber,
        DhlExpressImageUploadRequest request,
        CancellationToken cancellationToken = default);

    Task UploadInvoiceDataAsync(
        string accountReference,
        string shipmentTrackingNumber,
        DhlExpressInvoiceDataRequest request,
        CancellationToken cancellationToken = default);

    Task<DhlExpressPickupResponse> CreatePickupAsync(
        string accountReference,
        DhlExpressPickupRequest request,
        CancellationToken cancellationToken = default);

    Task<DhlExpressPickupUpdateResponse> UpdatePickupAsync(
        string accountReference,
        string dispatchConfirmationNumber,
        DhlExpressPickupUpdateRequest request,
        CancellationToken cancellationToken = default);

    Task CancelPickupAsync(
        string accountReference,
        string dispatchConfirmationNumber,
        string requestorName,
        string reason,
        CancellationToken cancellationToken = default);
}
