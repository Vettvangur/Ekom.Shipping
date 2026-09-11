namespace Ekom.Shipping.IcelandicPost;

public sealed class IcelandicPostOptions
{
    public const string SectionName = "Ekom:Shipping:IcelandicPost";

    public Dictionary<string, IcelandicPostAccountOptions> Accounts { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class IcelandicPostAccountOptions
{
    public string ApiUrl { get; init; } = string.Empty;

    public string ApiKey { get; init; } = string.Empty;

    public TimeSpan CacheDuration { get; init; } = TimeSpan.FromHours(3);
}
