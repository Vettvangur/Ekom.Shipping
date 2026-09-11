using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace Ekom.Shipping.Umbraco;

public sealed class EkomShippingComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Components().Append<EkomShippingEvents>();
        builder.Services.AddEkomShipping();
    }
}
