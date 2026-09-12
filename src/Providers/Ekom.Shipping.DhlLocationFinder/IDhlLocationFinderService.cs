namespace Ekom.Shipping.DhlLocationFinder;

public interface IDhlLocationFinderService
{
    Task<IReadOnlyList<DhlLocation>> FindByAddressAsync(
        string accountReference,
        DhlLocationAddressSearch request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DhlLocation>> FindByGeoAsync(
        string accountReference,
        DhlLocationGeoSearch request,
        CancellationToken cancellationToken = default);

    Task<DhlLocation> FindByKeywordIdAsync(
        string accountReference,
        string keywordId,
        string countryCode,
        string postalCode,
        CancellationToken cancellationToken = default);

    Task<DhlLocation> GetLocationAsync(
        string accountReference,
        string locationId,
        CancellationToken cancellationToken = default);
}
