namespace Ekom.Shipping.IcelandicPost;

public sealed class IcelandicPostOptions
{
    public const string SectionName = "Ekom:Shipping:IcelandicPost";

    public Dictionary<string, IcelandicPostAccountOptions> Accounts { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class IcelandicPostAccountOptions
{
    public ShippingFulfillmentMode FulfillmentMode { get; init; } = ShippingFulfillmentMode.Manual;

    public IcelandicPostLabelFormat LabelFormat { get; init; } = IcelandicPostLabelFormat.Unknown;

    public string ApiUrl { get; init; } = string.Empty;

    public string ApiKey { get; init; } = string.Empty;

    public TimeSpan CacheDuration { get; init; } = TimeSpan.FromHours(3);

    /// <summary>
    /// Where a shipment of this account can be followed, with <c>{trackingNumber}</c> in place of the number.
    /// When set, Ekom stores the link on the shipment and the manager shows it next to the tracking number.
    /// </summary>
    public string? TrackingUrlTemplate { get; init; }
}
