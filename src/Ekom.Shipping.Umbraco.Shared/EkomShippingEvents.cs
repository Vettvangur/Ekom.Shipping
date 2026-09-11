using Ekom.Events;
using Ekom.Shipping.Ekom;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Composing;

namespace Ekom.Shipping.Umbraco;

#if UMBRACO_13
internal sealed class EkomShippingEvents : IComponent
#else
internal sealed class EkomShippingEvents : IAsyncComponent
#endif
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EkomShippingEvents> _logger;

    public EkomShippingEvents(
        IServiceScopeFactory scopeFactory,
        ILogger<EkomShippingEvents> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

#if !UMBRACO_13
    public Task InitializeAsync(bool isRestarting, CancellationToken cancellationToken)
    {
        CheckoutEvents.CompleteCheckoutAsync += OnCompleteCheckoutAsync;
        return Task.CompletedTask;
    }

    public Task TerminateAsync(bool isRestarting, CancellationToken cancellationToken)
    {
        CheckoutEvents.CompleteCheckoutAsync -= OnCompleteCheckoutAsync;
        return Task.CompletedTask;
    }
#else
    public void Initialize()
    {
        CheckoutEvents.CompleteCheckoutAsync += OnCompleteCheckoutAsync;
    }

    public void Terminate()
    {
        CheckoutEvents.CompleteCheckoutAsync -= OnCompleteCheckoutAsync;
    }
#endif

    private async Task OnCompleteCheckoutAsync(
        object sender,
        CompleteCheckoutEventArgs args,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var fulfillment = scope.ServiceProvider.GetRequiredService<IShippingFulfillmentService>();
            await fulfillment.CreateAutomaticAsync(args.OrderInfo, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Automatic shipment creation failed during checkout completion for order {OrderId}",
                args.OrderInfo.UniqueId);
        }
    }
}
