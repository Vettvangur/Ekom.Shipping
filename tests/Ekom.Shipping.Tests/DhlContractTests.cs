using System.Net;
using System.Text;
using System.Text.Json;
using Ekom.Shipping.DhlExpress;
using Ekom.Shipping.DhlLocationFinder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ekom.Shipping.Tests;

public sealed class DhlContractTests
{
    [Fact]
    public async Task LocationFinder_UsesDocumentedEndpointHeaderAndRepeatedFilters()
    {
        var handler = new RecordingHandler("""
            {"locations":[{"url":"https://example.test/L1","location":{"ids":[{"locationId":"L1","provider":"express"}],"keyword":"","keywordId":"K1","type":"servicepoint"},"name":"DHL Point","place":{"address":{"countryCode":"IS","postalCode":"101","addressLocality":"Reykjavík","streetAddress":"Street 1"},"geo":{"latitude":64.1,"longitude":-21.9}},"serviceTypes":["express:pick-up"]}]}
            """);
        var service = BuildLocationFinder(handler);

        var locations = await service.FindByAddressAsync(
            "main",
            new DhlLocationAddressSearch(
                "IS",
                PostalCode: "101",
                ProviderTypes: ["express"],
                ServiceTypes: ["express:pick-up", "parking"]));

        var location = Assert.Single(locations);
        Assert.Equal("L1", location.ToPickupLocation("express").Id);
        Assert.Equal("secret", handler.Requests[0].ApiKey);
        Assert.Equal(
            "https://api.dhl.test/location-finder/v1/find-by-address?countryCode=IS&postalCode=101&providerType=express&serviceType=express%3Apick-up&serviceType=parking",
            handler.Requests[0].Uri.AbsoluteUri);
    }

    [Fact]
    public async Task DhlExpress_UsesBasicAuthenticationVersionAndProductsContract()
    {
        var handler = new RecordingHandler("""{"products":[]}""");
        var service = BuildExpress(handler);

        await service.GetProductsAsync(
            "main",
            new DhlExpressOnePieceRequest(
                "123456789",
                "IS",
                "Reykjavík",
                "DK",
                "Copenhagen",
                1,
                10,
                20,
                30,
                "2026-09-12",
                true,
                "metric"));

        var request = handler.Requests[0];
        Assert.Equal("Basic", request.AuthorizationScheme);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("user:pass")), request.AuthorizationParameter);
        Assert.Equal("3.3.2", request.Version);
        Assert.Contains("/products?accountNumber=123456789", request.Uri.AbsoluteUri);
        Assert.Contains("isCustomsDeclarable=true", request.Uri.Query);
    }

    [Fact]
    public async Task DhlExpress_BooksInternationalGoodsAndReturnsCreationLabel()
    {
        var label = Convert.ToBase64String("PDF"u8.ToArray());
        var handler = new RecordingHandler($$"""
            {"shipmentTrackingNumber":"1234567890","documents":[{"imageFormat":"PDF","content":"{{label}}","typeCode":"label","packageReferenceNumber":1}]}
            """);
        var service = BuildExpress(handler, new TestEnricher());

        var result = await ((IShippingFulfillmentCarrier)service).CreateShipmentAsync(
            "main",
            new ShipmentBookingRequest(
                "ORDER-1",
                "P",
                new ShipmentRecipient("Buyer", "buyer@example.test", "123", "Road 1", "2100", "Copenhagen", "DK"),
                [new ShipmentItem("SKU", "Book", 1)],
                100,
                "DKK"));

        Assert.Equal("1234567890", result.ShipmentId);
        var document = Assert.Single(result.Documents!);
        Assert.Equal("application/pdf", document.ContentType);
        Assert.Equal("PDF", Encoding.UTF8.GetString(document.Content));
        using var payload = JsonDocument.Parse(handler.Requests[0].Content!);
        Assert.True(payload.RootElement.GetProperty("content").GetProperty("isCustomsDeclarable").GetBoolean());
        Assert.Equal("Buyer", payload.RootElement.GetProperty("customerDetails")
            .GetProperty("receiverDetails").GetProperty("contactInformation").GetProperty("fullName").GetString());
        Assert.Equal(100m, payload.RootElement.GetProperty("content").GetProperty("declaredValue").GetDecimal());
        Assert.Equal("490199", payload.RootElement.GetProperty("content")
            .GetProperty("exportDeclaration").GetProperty("lineItems")[0]
            .GetProperty("commodityCodes")[0].GetProperty("value").GetString());
    }

    [Fact]
    public async Task DhlExpress_RejectsIncompleteInternationalCustomsBeforeSubmission()
    {
        var handler = new RecordingHandler("{}");
        var service = BuildExpress(handler);
        var request = BuildShipmentRequest() with
        {
            Content = BuildShipmentRequest().Content with { ExportDeclaration = null },
        };

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateShipmentAsync("main", request));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DhlExpress_TreatsServerErrorDuringShipmentCreationAsOutcomeUnknown()
    {
        var service = BuildExpress(new RecordingHandler("{}", HttpStatusCode.ServiceUnavailable));

        await Assert.ThrowsAsync<ShipmentOutcomeUnknownException>(() =>
            service.CreateShipmentAsync("main", BuildShipmentRequest()));
    }

    [Fact]
    public async Task DhlExpress_TreatsCancellationAfterMutationDispatchAsOutcomeUnknown()
    {
        var service = BuildExpress(new CancellingHandler());

        await Assert.ThrowsAsync<ShipmentOutcomeUnknownException>(() =>
            service.CreateShipmentAsync("main", BuildShipmentRequest()));
    }

    [Fact]
    public async Task DhlExpress_CancelsPickupWithoutAdvertisingShipmentDeletion()
    {
        var handler = new RecordingHandler(string.Empty);
        var service = BuildExpress(handler);

        await service.CancelPickupAsync("main", "CONFIRM-1", "Jane", "No parcels");

        Assert.IsNotAssignableFrom<IShippingShipmentDeletionCarrier>(service);
        Assert.Equal(HttpMethod.Delete, handler.Requests[0].Method);
        Assert.Equal(
            "https://express.test/mydhlapi/pickups/CONFIRM-1?requestorName=Jane&reason=No%20parcels",
            handler.Requests[0].Uri.AbsoluteUri);
    }

    [Fact]
    public async Task DhlExpress_SendsTrackingLanguageAndPreservesEventDetails()
    {
        var handler = new RecordingHandler("""
            {"shipments":[{"shipmentTrackingNumber":"1234567890","events":[{"typeCode":"OK","serviceArea":[{"code":"CPH"}],"remarks":[{"value":"Delivered","details":"Door"}]}],"pieces":[{"trackingNumber":"P1","actualWeight":1.2,"events":[]}]}]}
            """);
        var service = BuildExpress(handler);

        var tracking = await service.GetShipmentTrackingAsync("main", "1234567890", language: "isl");

        Assert.Equal("isl", handler.Requests[0].AcceptLanguage);
        var shipment = Assert.Single(tracking.Shipments);
        Assert.Equal("CPH", Assert.Single(Assert.Single(shipment.Events).ServiceArea).Code);
        Assert.Equal("Door", Assert.Single(shipment.Events[0].Remarks).Details);
        Assert.Equal(1.2m, Assert.Single(shipment.Pieces).ActualWeight);
    }

    [Fact]
    public async Task DhlExpress_TreatsNullCreationDocumentsAsOutcomeUnknown()
    {
        var service = BuildExpress(new RecordingHandler("""
            {"shipmentTrackingNumber":"1234567890","documents":null}
            """), new TestEnricher());

        await Assert.ThrowsAsync<ShipmentOutcomeUnknownException>(() =>
            ((IShippingFulfillmentCarrier)service).CreateShipmentAsync(
                "main",
                new ShipmentBookingRequest(
                    "ORDER-1",
                    "P",
                    new ShipmentRecipient("Buyer", "buyer@example.test", "123", "Road 1", "2100", "Copenhagen", "DK"),
                    [new ShipmentItem("SKU", "Book", 1)],
                    100,
                    "DKK")));
    }

    [Fact]
    public async Task DhlExpress_SerializesRequiredInvoiceFunction()
    {
        var label = Convert.ToBase64String("PDF"u8.ToArray());
        var handler = new RecordingHandler($$"""
            {"shipmentTrackingNumber":"1234567890","documents":[{"imageFormat":"PDF","content":"{{label}}","typeCode":"label"}]}
            """);
        var service = BuildExpress(handler);
        var original = BuildShipmentRequest();
        var request = original with
        {
            Content = original.Content with
            {
                ExportDeclaration = original.Content.ExportDeclaration! with
                {
                    Invoice = new DhlExpressInvoice("INV-1", "2026-09-11", "export"),
                },
            },
        };

        await service.CreateShipmentAsync("main", request);

        using var payload = JsonDocument.Parse(handler.Requests[0].Content!);
        Assert.Equal("export", payload.RootElement.GetProperty("content")
            .GetProperty("exportDeclaration").GetProperty("invoice").GetProperty("function").GetString());
    }

    private static IDhlLocationFinderService BuildLocationFinder(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDhlLocationFinder(Configuration(new Dictionary<string, string?>
        {
            ["Ekom:Shipping:DhlLocationFinder:Accounts:main:ApiUrl"] = "https://api.dhl.test/location-finder/v1/",
            ["Ekom:Shipping:DhlLocationFinder:Accounts:main:ApiKey"] = "secret",
        }));
        services.AddSingleton<IHttpClientFactory>(new TestHttpClientFactory(handler));
        return services.BuildServiceProvider().GetRequiredService<IDhlLocationFinderService>();
    }

    private static IDhlExpressShippingService BuildExpress(
        HttpMessageHandler handler,
        IDhlExpressShipmentEnricher? enricher = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDhlExpressShipping(Configuration(new Dictionary<string, string?>
        {
            ["Ekom:Shipping:DhlExpress:Accounts:main:ApiUrl"] = "https://express.test/mydhlapi/",
            ["Ekom:Shipping:DhlExpress:Accounts:main:ApiVersion"] = "3.3.2",
            ["Ekom:Shipping:DhlExpress:Accounts:main:Username"] = "user",
            ["Ekom:Shipping:DhlExpress:Accounts:main:Password"] = "pass",
            ["Ekom:Shipping:DhlExpress:Accounts:main:AccountNumber"] = "123456789",
            ["Ekom:Shipping:DhlExpress:Accounts:main:ProductCode"] = "P",
            ["Ekom:Shipping:DhlExpress:Accounts:main:Shipper:Name"] = "Warehouse",
            ["Ekom:Shipping:DhlExpress:Accounts:main:Shipper:CompanyName"] = "Merchant",
            ["Ekom:Shipping:DhlExpress:Accounts:main:Shipper:Phone"] = "5551234",
            ["Ekom:Shipping:DhlExpress:Accounts:main:Shipper:AddressLine1"] = "Origin 1",
            ["Ekom:Shipping:DhlExpress:Accounts:main:Shipper:PostalCode"] = "101",
            ["Ekom:Shipping:DhlExpress:Accounts:main:Shipper:CityName"] = "Reykjavík",
            ["Ekom:Shipping:DhlExpress:Accounts:main:Shipper:CountryCode"] = "IS",
        }));
        services.AddSingleton<IHttpClientFactory>(new TestHttpClientFactory(handler));
        if (enricher is not null)
        {
            services.AddSingleton(enricher);
        }

        return services.BuildServiceProvider().GetRequiredService<IDhlExpressShippingService>();
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static DhlExpressShipmentRequest BuildShipmentRequest() => new(
        "2026-09-12T10:00:00GMT+00:00",
        new DhlExpressPickupInstruction(false),
        "P",
        [new DhlExpressAccount("shipper", "123456789")],
        new DhlExpressShipmentCustomers(
            Party("Warehouse", "IS", "101", "Reykjavík"),
            Party("Buyer", "DK", "2100", "Copenhagen")),
        new DhlExpressShipmentContent(
            [new DhlExpressPackage(1, new DhlExpressDimensions(10, 20, 30), ReferenceNumber: 1)],
            true,
            "Books",
            "DAP",
            "metric",
            100,
            "DKK",
            new DhlExpressExportDeclaration(
                [new DhlExpressExportLineItem(
                    1,
                    "Book",
                    100,
                    new DhlExpressQuantity(1, "PCS"),
                    "IS",
                    new DhlExpressWeight(NetValue: 1),
                    [new DhlExpressCommodityCode("outbound", "490199")])])),
        OutputImageProperties: new DhlExpressOutputImageProperties(
            EncodingFormat: "pdf",
            ImageOptions: [new DhlExpressImageOption("label", IsRequested: true)]));

    private static DhlExpressParty Party(string name, string country, string postcode, string city) =>
        new(
            new DhlExpressAddress(postcode, city, country, "Street 1"),
            new DhlExpressContact("5551234", name, name, "mail@example.test"));

    private sealed class TestEnricher : IDhlExpressShipmentEnricher
    {
        public Task<DhlExpressShipmentRequest> EnrichAsync(
            DhlExpressShipmentEnrichmentContext context,
            CancellationToken cancellationToken = default)
        {
            var template = BuildShipmentRequest();
            var recipient = context.BookingRequest.Recipient;
            return Task.FromResult(template with
            {
                ProductCode = context.ProductCode,
                Accounts = [new DhlExpressAccount("shipper", context.AccountNumber)],
                CustomerDetails = new DhlExpressShipmentCustomers(
                    context.ConfiguredShipper,
                    new DhlExpressParty(
                        new DhlExpressAddress(
                            recipient.PostalCode,
                            recipient.City,
                            recipient.CountryCode,
                            recipient.Address),
                        new DhlExpressContact(
                            recipient.Phone,
                            recipient.Name,
                            recipient.Name,
                            recipient.Email))),
                Content = template.Content with
                {
                    DeclaredValue = context.BookingRequest.Value,
                    DeclaredValueCurrency = context.BookingRequest.Currency,
                },
            });
        }
    }

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RecordingHandler(
        string responseContent,
        HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter,
                request.Headers.TryGetValues("x-version", out var versions) ? versions.Single() : null,
                request.Headers.TryGetValues("DHL-API-Key", out var keys) ? keys.Single() : null,
                request.Headers.AcceptLanguage.SingleOrDefault()?.Value));
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseContent, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class CancellingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new OperationCanceledException("The dispatched request was cancelled.", cancellationToken);
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri Uri,
        string? Content,
        string? AuthorizationScheme,
        string? AuthorizationParameter,
        string? Version,
        string? ApiKey,
        string? AcceptLanguage);
}
