using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ekom.Shipping;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEkomShippingCore(this IServiceCollection services)
    {
        services.TryAddScoped<IShippingCarrierRegistry, ShippingCarrierRegistry>();
        services.TryAddScoped<IShippingSelectionValidator, ShippingSelectionValidator>();
        services.TryAddScoped<IShippingFulfillmentCarrierRegistry, ShippingFulfillmentCarrierRegistry>();
        return services;
    }
}
