using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ekom.Shipping.DhlLocationFinder;

public sealed record DhlLocationAddressSearch(
    string CountryCode,
    string? AddressLocality = null,
    string? PostalCode = null,
    string? StreetAddress = null,
    IReadOnlyList<string>? ProviderTypes = null,
    IReadOnlyList<string>? LocationTypes = null,
    IReadOnlyList<string>? ServiceTypes = null,
    decimal? Radius = null,
    decimal? Limit = null,
    bool? HideClosedLocations = null,
    string? CurrentDate = null);

public sealed record DhlLocationGeoSearch(
    double Latitude,
    double Longitude,
    IReadOnlyList<string>? ProviderTypes = null,
    IReadOnlyList<string>? LocationTypes = null,
    IReadOnlyList<string>? ServiceTypes = null,
    decimal? Radius = null,
    decimal? Limit = null,
    string? CountryCode = null,
    bool? HideClosedLocations = null,
    string? CurrentDate = null);

public sealed class DhlLocation
{
    public string Url { get; init; } = string.Empty;
    public DhlLocationIdentity Location { get; init; } = new();
    public string Name { get; init; } = string.Empty;
    public long? Distance { get; init; }
    public DhlRoutingDistance? RoutingDistance { get; init; }
    public DhlPlace Place { get; init; } = new();
    public IReadOnlyList<DhlOpeningHours> OpeningHours { get; init; } = [];
    public IReadOnlyList<DhlClosurePeriod> ClosurePeriods { get; init; } = [];
    public IReadOnlyList<string> ServiceTypes { get; init; } = [];
    public string? AvailableCapacity { get; init; }
    public IReadOnlyList<DhlAverageCapacity> AverageCapacityDayOfWeek { get; init; } = [];
    public IReadOnlyList<string> Labels { get; init; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; init; }

    public PickupLocation ToPickupLocation(string provider)
    {
        var id = Location.Ids.FirstOrDefault(candidate =>
            string.Equals(candidate.Provider, provider, StringComparison.OrdinalIgnoreCase))?.LocationId;
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ShippingProviderException(
                "dhl-location-finder",
                $"The location does not contain an identifier for provider '{provider}'.");
        }

        return new PickupLocation(
            id,
            Name,
            Place.Address.StreetAddress,
            Place.Address.PostalCode,
            Place.Address.AddressLocality,
            Place.Geo.Latitude,
            Place.Geo.Longitude,
            Location.KeywordId);
    }
}

public sealed class DhlLocationIdentity
{
    public IReadOnlyList<DhlLocationId> Ids { get; init; } = [];
    public string Keyword { get; init; } = string.Empty;
    public string KeywordId { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public bool? LeanLocker { get; init; }
    public bool? DfLocker { get; init; }
}

public sealed record DhlLocationId(string LocationId, string Provider);

public sealed class DhlPlace
{
    public DhlLocationAddress Address { get; init; } = new();
    public DhlGeo Geo { get; init; } = new();
    public DhlContainedPlace? ContainedInPlace { get; init; }
}

public sealed class DhlLocationAddress
{
    public string CountryCode { get; init; } = string.Empty;
    public string PostalCode { get; init; } = string.Empty;
    public string AddressLocality { get; init; } = string.Empty;
    public string StreetAddress { get; init; } = string.Empty;
}

public sealed record DhlGeo(double? Latitude = null, double? Longitude = null);
public sealed record DhlContainedPlace(string Name);
public sealed record DhlRoutingDistance(long Distance, string DistanceType);
public sealed record DhlOpeningHours(string Opens, string Closes, string DayOfWeek);
public sealed record DhlClosurePeriod(string Type, string FromDate, string ToDate);
public sealed record DhlAverageCapacity(string DayOfWeek, string Capacity);

public static class DhlLocationFinderValues
{
    public const string ExpressProvider = "express";
    public const string ParcelProvider = "parcel";
    public const string ExpressPickupService = "express:pick-up";
    public const string ExpressDropOffService = "express:drop-off";
}
