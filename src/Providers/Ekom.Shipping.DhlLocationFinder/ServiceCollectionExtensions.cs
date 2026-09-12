using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ekom.Shipping.DhlLocationFinder;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDhlLocationFinder(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddEkomShippingCore();
        services.AddHttpClient(nameof(DhlLocationFinderService), client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Ekom.Shipping.DhlLocationFinder/0.1");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        services.AddOptions<DhlLocationFinderOptions>()
            .Bind(configuration.GetSection(DhlLocationFinderOptions.SectionName))
            .Validate(HasValidAccounts, "Every DHL Location Finder account requires HTTPS and an API key.")
            .ValidateOnStart();
        services.AddSingleton<IDhlLocationFinderService, DhlLocationFinderService>();
        return services;
    }

    private static bool HasValidAccounts(DhlLocationFinderOptions options) =>
        options.Accounts.Count > 0 && options.Accounts.Values.All(account =>
            Uri.TryCreate(account.ApiUrl, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            !string.IsNullOrWhiteSpace(account.ApiKey));
}
