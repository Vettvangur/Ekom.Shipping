namespace Ekom.Shipping.Dropp;

public static class DroppShippingDefaults
{
    public const string CarrierAlias = "dropp";
    public const string PickupServiceId = "pickup";
    public const string HomeDeliveryServiceId = "home-delivery";

    public static readonly Guid HomeDeliveryLocationId =
        Guid.Parse("9ec1f30c-2564-4b73-8954-25b7b3186ed3");

    public static readonly Guid SamskipLocationId =
        Guid.Parse("a178c25e-bb35-4420-8792-d5295f0e7fcc");
}
