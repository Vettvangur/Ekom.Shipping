namespace Ekom.Shipping.Ekom;

public interface IEkomPickupLocationQueryService
{
    Task<IReadOnlyList<PickupLocation>?> GetAsync(
        Guid providerKey,
        string? storeAlias,
        string countryCode,
        string? postalCode,
        CancellationToken cancellationToken = default);
}
