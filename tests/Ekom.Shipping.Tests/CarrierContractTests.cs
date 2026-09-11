using System.Net;
using System.Text;
using System.Text.Json;
using Ekom.Shipping.Dropp;
using Ekom.Shipping.IcelandicPost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ekom.Shipping.Tests;

public sealed class CarrierContractTests
{
    [Fact]
    public async Task Dropp_BooksHomeDeliveryWithReservedBarcode()
    {
        var orderId = Guid.NewGuid();
        var handler = new SequenceHandler(
            """{"barcode":"BAR123"}""",
            $$"""{"id":"{{orderId}}"}""");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDroppShipping(BuildConfiguration(new Dictionary<string, string?>
        {
            ["Ekom:Shipping:Dropp:Accounts:main:ApiUrl"] = "https://dropp.test/api/",
            ["Ekom:Shipping:Dropp:Accounts:main:ApiKey"] = "secret",
            ["Ekom:Shipping:Dropp:Accounts:main:StoreId"] = "store",
        }));
        services.AddSingleton<IHttpClientFactory>(new TestHttpClientFactory(handler));
        await using var provider = services.BuildServiceProvider();
        var carrier = provider.GetRequiredService<IShippingFulfillmentCarrierRegistry>()
            .GetRequired(DroppShippingDefaults.CarrierAlias);

        var barcode = await carrier.ReserveBookingReferenceAsync("main");
        var result = await carrier.CreateShipmentAsync(
            "main",
            new ShipmentBookingRequest(
                "ORDER-1",
                DroppShippingDefaults.HomeDeliveryServiceId,
                new ShipmentRecipient("Customer", "customer@example.test", "5551234", "Street 1", "101", "Reykjavík", "IS"),
                [new ShipmentItem("SKU1", "Product", 2)],
                1200m,
                "ISK",
                BookingReference: barcode));

        Assert.Equal(orderId.ToString(), result.ShipmentId);
        Assert.Equal("BAR123", result.BookingReference);
        Assert.Equal("https://dropp.test/api/orders/barcode/", handler.Requests[0].Uri.AbsoluteUri);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        using var document = JsonDocument.Parse(handler.Requests[1].Content!);
        Assert.Equal("BAR123", document.RootElement.GetProperty("barcode").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("products")[0].GetProperty("quantity").GetInt32());
        Assert.False(document.RootElement.TryGetProperty("id", out _));
    }

    [Fact]
    public async Task Dropp_MapsLocationsAndSendsConfiguredAuthentication()
    {
        var handler = new RecordingHandler("""
            {"locations":[{"id":"loc-1","name":"Locker","address":"Street 1","externalLocationId":"DROPP1","addressObject":{"zip":101,"town":"Reykjavík"}}]}
            """);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDroppShipping(BuildConfiguration(new Dictionary<string, string?>
        {
            ["Ekom:Shipping:Dropp:Accounts:main:ApiUrl"] = "https://dropp.test/api/",
            ["Ekom:Shipping:Dropp:Accounts:main:ApiKey"] = "secret",
            ["Ekom:Shipping:Dropp:Accounts:main:StoreId"] = "store id",
        }));
        services.AddSingleton<IHttpClientFactory>(new TestHttpClientFactory(handler));
        await using var provider = services.BuildServiceProvider();
        var carrier = provider.GetRequiredService<IShippingCarrierRegistry>()
            .GetRequired(DroppShippingDefaults.CarrierAlias);

        var locations = await carrier.GetPickupLocationsAsync(
            "main",
            DroppShippingDefaults.PickupServiceId,
            new ShippingLookupRequest("store", "IS"));

        var location = Assert.Single(locations);
        Assert.Equal("DROPP1", location.ExternalId);
        Assert.Equal("https://dropp.test/api/dropp/locations?store=store%20id", handler.RequestUri?.AbsoluteUri);
        Assert.Equal("Basic", handler.AuthorizationScheme);
        Assert.Equal("secret", handler.AuthorizationParameter);
    }

    [Fact]
    public async Task Dropp_TreatsServerErrorAfterSubmissionAsOutcomeUnknown()
    {
        var carrier = BuildDroppCarrier(new StatusHandler(HttpStatusCode.ServiceUnavailable));

        await Assert.ThrowsAsync<ShipmentOutcomeUnknownException>(() => carrier.CreateShipmentAsync(
            "main",
            BuildDroppBookingRequest()));
    }

    [Fact]
    public async Task Dropp_TreatsValidationErrorAsDefiniteFailure()
    {
        var carrier = BuildDroppCarrier(new StatusHandler(HttpStatusCode.BadRequest));

        await Assert.ThrowsAsync<ShippingProviderException>(() => carrier.CreateShipmentAsync(
            "main",
            BuildDroppBookingRequest()));
    }

    [Fact]
    public async Task IcelandicPost_MapsAvailableServices()
    {
        var handler = new RecordingHandler("""
            {"deliveryServicesAndPrices":[{"deliveryServiceId":"DPO","nameLong":"Póstbox"}]}
            """);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIcelandicPostShipping(BuildConfiguration(new Dictionary<string, string?>
        {
            ["Ekom:Shipping:IcelandicPost:Accounts:main:ApiUrl"] = "https://post.test/api/",
            ["Ekom:Shipping:IcelandicPost:Accounts:main:ApiKey"] = "secret",
        }));
        services.AddSingleton<IHttpClientFactory>(new TestHttpClientFactory(handler));
        await using var provider = services.BuildServiceProvider();
        var carrier = provider.GetRequiredService<IShippingCarrierRegistry>()
            .GetRequired(IcelandicPostShippingDefaults.CarrierAlias);

        var availableServices = await carrier.GetServicesAsync(
            "main",
            new ShippingLookupRequest("store", "IS", "101"));

        var service = Assert.Single(availableServices);
        Assert.True(service.RequiresPickupLocation);
        Assert.Equal("secret", handler.ApiKey);
        Assert.Equal(
            "https://post.test/api/wscm/v1/deliveryservicesandprices?countryCode=IS&postCode=101",
            handler.RequestUri?.AbsoluteUri);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static IShippingFulfillmentCarrier BuildDroppCarrier(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDroppShipping(BuildConfiguration(new Dictionary<string, string?>
        {
            ["Ekom:Shipping:Dropp:Accounts:main:ApiUrl"] = "https://dropp.test/api/",
            ["Ekom:Shipping:Dropp:Accounts:main:ApiKey"] = "secret",
            ["Ekom:Shipping:Dropp:Accounts:main:StoreId"] = "store",
        }));
        services.AddSingleton<IHttpClientFactory>(new TestHttpClientFactory(handler));
        return services.BuildServiceProvider()
            .GetRequiredService<IShippingFulfillmentCarrierRegistry>()
            .GetRequired(DroppShippingDefaults.CarrierAlias);
    }

    private static ShipmentBookingRequest BuildDroppBookingRequest() => new(
        "ORDER-1",
        DroppShippingDefaults.HomeDeliveryServiceId,
        new ShipmentRecipient("Customer", "customer@example.test", "5551234", "Street 1", "101", "Reykjavík", "IS"),
        [new ShipmentItem("SKU1", "Product", 1)],
        1200m,
        "ISK",
        BookingReference: "BAR123");

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public TestHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _responseJson;

        public RecordingHandler(string responseJson)
        {
            _responseJson = responseJson;
        }

        public Uri? RequestUri { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        public string? ApiKey { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            ApiKey = request.Headers.TryGetValues("x-api-key", out var values)
                ? values.Single()
                : null;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses;

        public SequenceHandler(params string[] responses)
        {
            _responses = new Queue<string>(responses);
        }

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var content = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, content));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responses.Dequeue(), Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class StatusHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode));
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Content);
}
