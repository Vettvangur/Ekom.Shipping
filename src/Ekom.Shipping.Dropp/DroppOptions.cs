namespace Ekom.Shipping.Dropp;

public sealed class DroppOptions
{
    public const string SectionName = "Ekom:Shipping:Dropp";

    public Dictionary<string, DroppAccountOptions> Accounts { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DroppAccountOptions
{
    public string ApiUrl { get; init; } = string.Empty;

    public string ApiKey { get; init; } = string.Empty;

    public string StoreId { get; init; } = string.Empty;

    public TimeSpan CacheDuration { get; init; } = TimeSpan.FromHours(6);
}
