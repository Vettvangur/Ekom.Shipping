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

    public Guid HomeDeliveryLocationId { get; init; } =
        Guid.Parse("9ec1f30c-2564-4b73-8954-25b7b3186ed3");
}
