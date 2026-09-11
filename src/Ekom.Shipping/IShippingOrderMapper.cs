using Ekom.Models;

namespace Ekom.Shipping.Ekom;

public interface IShippingOrderMapper
{
    ShipmentBookingRequest Map(
        IOrderInfo order,
        EkomShippingMethodConfiguration configuration,
        string? bookingReference);
}
