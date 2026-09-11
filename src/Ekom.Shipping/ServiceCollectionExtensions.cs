using Ekom.Shipping.Ekom;
using Ekom.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ekom.Shipping;

public static class EkomServiceCollectionExtensions
{
    public static IServiceCollection AddEkomShipping(this IServiceCollection services)
    {
        services.AddEkomShippingCore();
        services.AddScoped<IEkomShippingCheckoutService, EkomShippingCheckoutService>();
        services.TryAddScoped<IShippingOrderMapper, ShippingOrderMapper>();
        services.AddScoped<IShippingFulfillmentService, ShippingFulfillmentService>();
        services.AddTransient<IOrderManagerActionProvider, ShippingOrderManagerActionProvider>();
        return services;
    }
}
