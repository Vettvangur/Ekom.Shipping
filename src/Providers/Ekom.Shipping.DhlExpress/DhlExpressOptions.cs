namespace Ekom.Shipping.DhlExpress;

public sealed class DhlExpressOptions
{
    public const string SectionName = "Ekom:Shipping:DhlExpress";

    public Dictionary<string, DhlExpressAccountOptions> Accounts { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DhlExpressAccountOptions
{
    public string ApiUrl { get; init; } = "https://express.api.dhl.com/mydhlapi/";
    public string ApiVersion { get; init; } = "3.3.2";
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string AccountNumber { get; init; } = string.Empty;
    public string ProductCode { get; init; } = string.Empty;
    public string ProductName { get; init; } = "DHL Express";
    public ShippingFulfillmentMode FulfillmentMode { get; init; } = ShippingFulfillmentMode.Manual;
    public DhlExpressShipperOptions Shipper { get; init; } = new();
}

public sealed class DhlExpressShipperOptions
{
    public string Name { get; init; } = string.Empty;
    public string CompanyName { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string AddressLine1 { get; init; } = string.Empty;
    public string? AddressLine2 { get; init; }
    public string PostalCode { get; init; } = string.Empty;
    public string CityName { get; init; } = string.Empty;
    public string CountryCode { get; init; } = string.Empty;
    public string? ProvinceCode { get; init; }
}
