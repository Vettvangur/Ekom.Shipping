using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ekom.Shipping.DhlExpress;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDhlExpressShipping(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddEkomShippingCore();
        services.AddHttpClient(nameof(DhlExpressCarrier), client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Ekom.Shipping.DhlExpress/0.1");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        services.AddOptions<DhlExpressOptions>()
            .Bind(configuration.GetSection(DhlExpressOptions.SectionName))
            .Validate(HasValidAccounts, "Every DHL Express account requires valid API, billing, product, and shipper settings.")
            .ValidateOnStart();
        services.AddSingleton<DhlExpressCarrier>();
        services.AddSingleton<IDhlExpressShippingService>(provider => provider.GetRequiredService<DhlExpressCarrier>());
        services.AddSingleton<IShippingCarrier>(provider => provider.GetRequiredService<DhlExpressCarrier>());
        services.AddSingleton<IShippingFulfillmentCarrier>(provider => provider.GetRequiredService<DhlExpressCarrier>());
        return services;
    }

    private static bool HasValidAccounts(DhlExpressOptions options) =>
        options.Accounts.Count > 0 && options.Accounts.Values.All(account =>
            Uri.TryCreate(account.ApiUrl, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            string.Equals(account.ApiVersion, DhlExpressDefaults.ApiVersion, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(account.Username) &&
            !string.IsNullOrWhiteSpace(account.Password) &&
            !string.IsNullOrWhiteSpace(account.AccountNumber) &&
            !string.IsNullOrWhiteSpace(account.ProductCode) &&
            Enum.IsDefined(account.FulfillmentMode) &&
            ValidShipper(account.Shipper));

    private static bool ValidShipper(DhlExpressShipperOptions shipper) =>
        !string.IsNullOrWhiteSpace(shipper.Name) &&
        !string.IsNullOrWhiteSpace(shipper.CompanyName) &&
        !string.IsNullOrWhiteSpace(shipper.Phone) &&
        !string.IsNullOrWhiteSpace(shipper.AddressLine1) &&
        !string.IsNullOrWhiteSpace(shipper.CityName) &&
        !string.IsNullOrWhiteSpace(shipper.CountryCode);
}
