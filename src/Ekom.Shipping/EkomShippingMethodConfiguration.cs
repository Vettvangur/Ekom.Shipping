using Ekom.Models;

namespace Ekom.Shipping.Ekom;

public sealed record EkomShippingMethodConfiguration(
    string CarrierAlias,
    string AccountReference,
    string ServiceId)
{
    public static EkomShippingMethodConfiguration? FromProvider(IShippingProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        provider.Properties.TryGetValue(EkomShippingPropertyAliases.CarrierAlias, out var carrierAlias);
        provider.Properties.TryGetValue(EkomShippingPropertyAliases.AccountReference, out var accountReference);
        provider.Properties.TryGetValue(EkomShippingPropertyAliases.ServiceId, out var serviceId);

        if (string.IsNullOrWhiteSpace(carrierAlias) &&
            string.IsNullOrWhiteSpace(accountReference) &&
            string.IsNullOrWhiteSpace(serviceId))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(carrierAlias) ||
            string.IsNullOrWhiteSpace(accountReference) ||
            string.IsNullOrWhiteSpace(serviceId))
        {
            throw new ShippingConfigurationException(
                $"Ekom shipping provider '{provider.Key}' has incomplete carrier configuration.");
        }

        return new EkomShippingMethodConfiguration(carrierAlias, accountReference, serviceId);
    }
}
