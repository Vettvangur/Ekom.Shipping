using Ekom.Shipping.Ekom;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Extensions;

namespace Ekom.Shipping.Umbraco;

public sealed class EkomShippingComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.AddMvcAndRazor(options =>
        {
            options.ConfigureApplicationPartManager(manager =>
            {
                var assembly = typeof(EkomPickupLocationsController).Assembly;
                if (!manager.ApplicationParts.OfType<AssemblyPart>().Any(x => x.Assembly == assembly))
                {
                    manager.ApplicationParts.Add(new AssemblyPart(assembly));
                }
            });
        });
        builder.Components().Append<EkomShippingEvents>();
        builder.Services.AddEkomShipping();
    }
}
