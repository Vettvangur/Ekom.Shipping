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
            """{"status":0,"message":"","codes":[{"code":101,"town":"Reykjavík","capital":true}],"flytjandicodes":[]}""",
            $$"""{"id":"{{orderId}}","barcode":"BAR123"}""");
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
        Assert.Equal("https://dropp.test/api/dropp/location/deliveryzips", handler.Requests[1].Uri.AbsoluteUri);
        Assert.Equal(HttpMethod.Post, handler.Requests[2].Method);
        using var document = JsonDocument.Parse(handler.Requests[2].Content!);
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

        await Assert.ThrowsAsync<ShipmentOutcomeUnknownException>(() => carrier.CreateOrderAsync(
            "main",
            BuildDroppCreateOrderRequest()));
    }

    [Fact]
    public async Task Dropp_TreatsValidationErrorAsDefiniteFailure()
    {
        var carrier = BuildDroppCarrier(new StatusHandler(HttpStatusCode.BadRequest));

        await Assert.ThrowsAsync<ShippingProviderException>(() => carrier.CreateOrderAsync(
            "main",
            BuildDroppCreateOrderRequest()));
    }

    [Fact]
    public async Task Dropp_CreatesOrderWithDocumentedOptionalFields()
    {
        var orderId = Guid.NewGuid();
        var handler = new SequenceHandler($$"""{"id":"{{orderId}}","barcode":"GENERATED"}""");
        var carrier = BuildDroppCarrier(handler);

        var result = await carrier.CreateOrderAsync(
            "main",
            BuildDroppCreateOrderRequest() with
            {
                Barcode = null,
                DayDelivery = true,
                Comment = "Ring bell",
                ReturnOrder = true,
            });

        Assert.Equal("GENERATED", result.Barcode);
        using var document = JsonDocument.Parse(handler.Requests[0].Content!);
        Assert.True(document.RootElement.GetProperty("daydelivery").GetBoolean());
        Assert.True(document.RootElement.GetProperty("returnorder").GetBoolean());
        Assert.Equal("Ring bell", document.RootElement.GetProperty("comment").GetString());
        Assert.False(document.RootElement.TryGetProperty("barcode", out _));
    }

    [Fact]
    public async Task Dropp_GetsAndUpdatesOrder()
    {
        var orderId = Guid.NewGuid();
        var handler = new SequenceHandler(
            $$"""{"id":"{{orderId}}","barcode":"BAR123","status":"initial"}""",
            "{}");
        var carrier = BuildDroppCarrier(handler);

        var order = await carrier.GetOrderAsync("main", orderId);
        await carrier.UpdateOrderAsync("main", orderId, new DroppUpdateOrderRequest(Barcode: "BAR456"));

        Assert.Equal("initial", order.Status);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal(HttpMethod.Patch, handler.Requests[1].Method);
        using var document = JsonDocument.Parse(handler.Requests[1].Content!);
        Assert.Equal("BAR456", document.RootElement.GetProperty("barcode").GetString());
        Assert.False(document.RootElement.TryGetProperty("customer", out _));
    }

    [Fact]
    public async Task Dropp_DeletesOnlyInitialOrder()
    {
        var orderId = Guid.NewGuid();
        var handler = new SequenceHandler(
            $$"""{"id":"{{orderId}}","barcode":"BAR123","status":"initial"}""",
            "{}");
        var carrier = BuildDroppCarrier(handler);

        await carrier.DeleteOrderAsync("main", orderId);

        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal(HttpMethod.Delete, handler.Requests[1].Method);
    }

    [Fact]
    public async Task Dropp_RejectsDeletionAfterCollection()
    {
        var orderId = Guid.NewGuid();
        var handler = new SequenceHandler(
            $$"""{"id":"{{orderId}}","barcode":"BAR123","status":"transit"}""");
        var carrier = BuildDroppCarrier(handler);

        await Assert.ThrowsAsync<ShippingProviderException>(() => carrier.DeleteOrderAsync("main", orderId));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Dropp_TreatsUncertainDeletionAsOutcomeUnknown()
    {
        var orderId = Guid.NewGuid();
        var handler = new ResponseSequenceHandler(
            (HttpStatusCode.OK, $$"""{"id":"{{orderId}}","barcode":"BAR123","status":"initial"}"""),
            (HttpStatusCode.ServiceUnavailable, ""));
        var carrier = BuildDroppCarrier(handler);

        await Assert.ThrowsAsync<ShipmentOutcomeUnknownException>(() =>
            carrier.DeleteOrderAsync("main", orderId));
    }

    [Fact]
    public async Task Dropp_FiltersUnavailableHomeDelivery()
    {
        var handler = new SequenceHandler(
            """{"status":0,"codes":[{"code":101,"town":"Reykjavík"}],"flytjandicodes":[{"code":190,"town":"Vogar"}]}""");
        var carrier = BuildDroppCarrier(handler);

        var services = await carrier.GetServicesAsync(
            "main",
            new ShippingLookupRequest("store", "IS", "600"));
        var postalCodes = await carrier.GetDeliveryPostalCodesAsync("main");

        Assert.Equal(DroppShippingDefaults.PickupServiceId, Assert.Single(services).Id);
        Assert.Equal((short)190, Assert.Single(postalCodes.FlytjandiCodes).Code);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Dropp_ReturnsShipmentAsJsonDocument()
    {
        var orderId = Guid.NewGuid();
        var handler = new SequenceHandler(
            $$"""{"id":"{{orderId}}","barcode":"BAR123","status":"initial"}""");
        var carrier = BuildDroppCarrier(handler);

        var document = await ((IShippingShipmentLookupCarrier)carrier).GetShipmentAsync(
            "main",
            orderId.ToString());

        Assert.Equal("application/json", document.ContentType);
        Assert.Equal($"dropp-order-{orderId}.json", document.FileName);
        Assert.Contains("BAR123", Encoding.UTF8.GetString(document.Content));
    }

    [Fact]
    public async Task Dropp_UsesDocumentedReturnAndExtraPackageEndpoints()
    {
        var orderId = Guid.NewGuid();
        var handler = new SequenceHandler("PDF", "PDF", "{}");
        var carrier = BuildDroppCarrier(handler);

        await carrier.GetReturnLabelAsync("main", "RETURN-1");
        await carrier.CreateExtraPackageLabelAsync("main", orderId);
        await carrier.DeleteExtraOrderAsync("main", orderId, "EXTRA-1");

        Assert.Equal("https://dropp.test/api/orders/returnpdf/RETURN-1", handler.Requests[0].Uri.AbsoluteUri);
        Assert.Equal("https://dropp.test/api/orders/extrapdf/" + orderId, handler.Requests[1].Uri.AbsoluteUri);
        Assert.Equal(
            $"https://dropp.test/api/orders/deleteextraorder/{orderId}/EXTRA-1/",
            handler.Requests[2].Uri.AbsoluteUri);
    }

    [Fact]
    public void Dropp_ReadsFulfillmentModeFromAccountConfiguration()
    {
        var automaticCarrier = BuildDroppCarrier(
            new StatusHandler(HttpStatusCode.OK),
            "Automatic");
        var manualCarrier = BuildDroppCarrier(new StatusHandler(HttpStatusCode.OK));

        Assert.Equal(ShippingFulfillmentMode.Automatic, automaticCarrier.GetFulfillmentMode("main"));
        Assert.Equal(ShippingFulfillmentMode.Manual, manualCarrier.GetFulfillmentMode("main"));
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

    private static IDroppShippingService BuildDroppCarrier(
        HttpMessageHandler handler,
        string? fulfillmentMode = null)
    {
        var configuration = new Dictionary<string, string?>
        {
            ["Ekom:Shipping:Dropp:Accounts:main:ApiUrl"] = "https://dropp.test/api/",
            ["Ekom:Shipping:Dropp:Accounts:main:ApiKey"] = "secret",
            ["Ekom:Shipping:Dropp:Accounts:main:StoreId"] = "store",
        };
        if (fulfillmentMode is not null)
        {
            configuration["Ekom:Shipping:Dropp:Accounts:main:FulfillmentMode"] = fulfillmentMode;
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDroppShipping(BuildConfiguration(configuration));
        services.AddSingleton<IHttpClientFactory>(new TestHttpClientFactory(handler));
        return services.BuildServiceProvider().GetRequiredService<IDroppShippingService>();
    }

    private static ShipmentBookingRequest BuildDroppBookingRequest() => new(
        "ORDER-1",
        DroppShippingDefaults.HomeDeliveryServiceId,
        new ShipmentRecipient("Customer", "customer@example.test", "5551234", "Street 1", "101", "Reykjavík", "IS"),
        [new ShipmentItem("SKU1", "Product", 1)],
        1200m,
        "ISK",
        BookingReference: "BAR123");

    private static DroppCreateOrderRequest BuildDroppCreateOrderRequest() => new(
        Guid.NewGuid(),
        1200m,
        [new DroppProduct("Product", "SKU1")],
        new DroppCustomer("Customer", "Street 1", "5551234", 101, "Reykjavík", "customer@example.test"),
        "BAR123");

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

    private sealed class ResponseSequenceHandler(
        params (HttpStatusCode StatusCode, string Content)[] responses) : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode StatusCode, string Content)> _responses = new(responses);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = _responses.Dequeue();
            return Task.FromResult(new HttpResponseMessage(response.StatusCode)
            {
                Content = new StringContent(response.Content, Encoding.UTF8, "application/json"),
            });
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
