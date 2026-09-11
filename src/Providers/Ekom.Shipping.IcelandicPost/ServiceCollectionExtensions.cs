using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ekom.Shipping.IcelandicPost;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIcelandicPostShipping(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddEkomShippingCore();
        services.AddMemoryCache();
        services.AddHttpClient(nameof(IcelandicPostCarrier), client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Ekom.Shipping.IcelandicPost/0.1");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
            });
        services.AddOptions<IcelandicPostOptions>()
            .Bind(configuration.GetSection(IcelandicPostOptions.SectionName))
            .Validate(
                HasValidAccounts,
                "Every Íslandspóstur account requires HTTPS, credentials, and a positive cache duration.")
            .ValidateOnStart();
        services.AddSingleton<IcelandicPostCarrier>();
        services.AddSingleton<IIcelandicPostShippingService>(
            provider => provider.GetRequiredService<IcelandicPostCarrier>());
        services.AddSingleton<IShippingCarrier>(provider => provider.GetRequiredService<IcelandicPostCarrier>());
        return services;
    }

    private static bool HasValidAccounts(IcelandicPostOptions options) =>
        options.Accounts.Count > 0 && options.Accounts.Values.All(account =>
            Uri.TryCreate(account.ApiUrl, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            !string.IsNullOrWhiteSpace(account.ApiKey) &&
            account.CacheDuration > TimeSpan.Zero);
}
