using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ekom.Shipping.Dropp;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDroppShipping(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddEkomShippingCore();
        services.AddMemoryCache();
        services.AddHttpClient(nameof(DroppCarrier), client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Ekom.Shipping.Dropp/0.1");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
            });
        services.AddOptions<DroppOptions>()
            .Bind(configuration.GetSection(DroppOptions.SectionName))
            .Validate(HasValidAccounts, "Every Dropp account requires HTTPS, credentials, and a positive cache duration.")
            .ValidateOnStart();
        services.AddSingleton<DroppCarrier>();
        services.AddSingleton<IDroppShippingService>(provider => provider.GetRequiredService<DroppCarrier>());
        services.AddSingleton<IShippingCarrier>(provider => provider.GetRequiredService<DroppCarrier>());
        services.AddSingleton<IShippingFulfillmentCarrier>(provider => provider.GetRequiredService<DroppCarrier>());
        return services;
    }

    private static bool HasValidAccounts(DroppOptions options) =>
        options.Accounts.Count > 0 && options.Accounts.Values.All(account =>
            Uri.TryCreate(account.ApiUrl, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            !string.IsNullOrWhiteSpace(account.ApiKey) &&
            !string.IsNullOrWhiteSpace(account.StoreId) &&
            account.CacheDuration > TimeSpan.Zero);
}
