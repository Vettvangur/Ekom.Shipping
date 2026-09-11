using Ekom.Shipping.Ekom;
using Microsoft.Extensions.DependencyInjection;

namespace Ekom.Shipping;

public static class EkomServiceCollectionExtensions
{
    public static IServiceCollection AddEkomShipping(this IServiceCollection services)
    {
        services.AddEkomShippingCore();
        services.AddScoped<IEkomShippingCheckoutService, EkomShippingCheckoutService>();
        return services;
    }
}
