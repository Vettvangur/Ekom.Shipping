namespace Ekom.Shipping;

[Flags]
public enum ShippingCarrierCapabilities
{
    None = 0,
    Services = 1,
    PickupLocations = 2,
}
