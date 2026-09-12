namespace Ekom.Shipping.DhlExpress;

public sealed record DhlExpressShipmentEnrichmentContext(
    string AccountReference,
    string AccountNumber,
    string ProductCode,
    DhlExpressParty ConfiguredShipper,
    ShipmentBookingRequest BookingRequest);

public interface IDhlExpressShipmentEnricher
{
    Task<DhlExpressShipmentRequest> EnrichAsync(
        DhlExpressShipmentEnrichmentContext context,
        CancellationToken cancellationToken = default);
}
