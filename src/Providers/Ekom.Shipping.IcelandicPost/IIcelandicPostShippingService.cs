namespace Ekom.Shipping.IcelandicPost;

public interface IIcelandicPostShippingService :
    IShippingCarrier,
    IShippingFulfillmentCarrier,
    IShippingShipmentLookupCarrier,
    IShippingShipmentDeletionCarrier
{
    Task<IReadOnlyList<IcelandicPostDeliveryService>> GetDeliveryServicesAsync(
        string accountReference,
        IcelandicPostServiceRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IcelandicPostPickupLocation>> GetPostboxesAsync(
        string accountReference,
        int? postcode = null,
        int? maxResults = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IcelandicPostPickupLocation>> GetParcelPointsAsync(
        string accountReference,
        int? postcode = null,
        int? maxResults = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IcelandicPostPickupLocation>> GetPostOfficesAsync(
        string accountReference,
        CancellationToken cancellationToken = default);

    Task<IcelandicPostShipment> CreateShipmentAsync(
        string accountReference,
        IcelandicPostShipmentRequest request,
        CancellationToken cancellationToken = default);

    Task<IcelandicPostShipment> GetShipmentAsync(
        string accountReference,
        string shipmentId,
        string language = "IS",
        CancellationToken cancellationToken = default);

    Task DeleteShipmentAsync(
        string accountReference,
        string shipmentId,
        string language = "IS",
        CancellationToken cancellationToken = default);

    Task<IcelandicPostShipmentStatus> GetShipmentStatusAsync(
        string accountReference,
        string shipmentId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IcelandicPostShipmentReference>> GetShipmentsByReferenceAsync(
        string accountReference,
        string reference,
        CancellationToken cancellationToken = default);

    Task<IcelandicPostShipmentList> GetShipmentsAsync(
        string accountReference,
        DateTime? startDate = null,
        DateTime? endDate = null,
        CancellationToken cancellationToken = default);

    Task<ShippingLabel> GetCombinedLabelsAsync(
        string accountReference,
        IReadOnlyCollection<string> shipmentIds,
        IcelandicPostLabelFormat format = IcelandicPostLabelFormat.A4Size,
        CancellationToken cancellationToken = default);

    Task<IcelandicPostDocument> GetProofOfDeliveryAsync(
        string accountReference,
        string shipmentId,
        CancellationToken cancellationToken = default);

    Task PrintAsync(
        string accountReference,
        string shipmentId,
        int? printerId = null,
        IcelandicPostLabelFormat outputFormat = IcelandicPostLabelFormat.LabelSize9x15,
        CancellationToken cancellationToken = default);
}
