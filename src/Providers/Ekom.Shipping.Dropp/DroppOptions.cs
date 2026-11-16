namespace Ekom.Shipping.Dropp;

public sealed class DroppOptions
{
    public const string SectionName = "Ekom:Shipping:Dropp";

    public Dictionary<string, DroppAccountOptions> Accounts { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DroppAccountOptions
{
    public ShippingFulfillmentMode FulfillmentMode { get; init; } = ShippingFulfillmentMode.Manual;

    public string ApiUrl { get; init; } = string.Empty;

    public string ApiKey { get; init; } = string.Empty;

    public string StoreId { get; init; } = string.Empty;

    public TimeSpan CacheDuration { get; init; } = TimeSpan.FromHours(6);

    public Guid HomeDeliveryLocationId { get; init; } = DroppShippingDefaults.HomeDeliveryLocationId;

    /// <summary>
    /// Where a shipment of this account can be followed, with <c>{trackingNumber}</c> in place of the number.
    /// When set, Ekom stores the link on the shipment and the manager shows it next to the tracking number.
    /// </summary>
    public string? TrackingUrlTemplate { get; init; }
}
