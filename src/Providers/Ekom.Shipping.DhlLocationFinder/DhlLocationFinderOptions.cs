namespace Ekom.Shipping.DhlLocationFinder;

public sealed class DhlLocationFinderOptions
{
    public const string SectionName = "Ekom:Shipping:DhlLocationFinder";

    public Dictionary<string, DhlLocationFinderAccountOptions> Accounts { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DhlLocationFinderAccountOptions
{
    public string ApiUrl { get; init; } = "https://api.dhl.com/location-finder/v1/";

    public string ApiKey { get; init; } = string.Empty;
}
