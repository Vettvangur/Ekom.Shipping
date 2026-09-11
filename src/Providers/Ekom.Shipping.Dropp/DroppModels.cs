using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ekom.Shipping.Dropp;

public sealed record DroppProduct(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("barcode")] string Barcode,
    [property: JsonPropertyName("quantity")] int Quantity = 1);

public sealed record DroppCustomer(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("address")] string Address,
    [property: JsonPropertyName("phoneNumber")] string PhoneNumber,
    [property: JsonPropertyName("zipcode")] short Zipcode,
    [property: JsonPropertyName("town")] string Town,
    [property: JsonPropertyName("emailAddress")] string? EmailAddress = null,
    [property: JsonPropertyName("socialSecurityNumber")] string? SocialSecurityNumber = null);

public sealed record DroppCreateOrderRequest(
    [property: JsonPropertyName("locationId")] Guid LocationId,
    [property: JsonPropertyName("value")] decimal Value,
    [property: JsonPropertyName("products")] IReadOnlyList<DroppProduct> Products,
    [property: JsonPropertyName("customer")] DroppCustomer Customer,
    [property: JsonPropertyName("barcode")] string? Barcode = null,
    [property: JsonPropertyName("daydelivery")] bool? DayDelivery = null,
    [property: JsonPropertyName("comment")] string? Comment = null,
    [property: JsonPropertyName("returnorder")] bool? ReturnOrder = null);

public sealed record DroppUpdateOrderRequest(
    [property: JsonPropertyName("barcode")] string? Barcode = null,
    [property: JsonPropertyName("locationId")] Guid? LocationId = null,
    [property: JsonPropertyName("customer")] DroppCustomer? Customer = null,
    [property: JsonPropertyName("products")] IReadOnlyList<DroppProduct>? Products = null);

public sealed class DroppOrder
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("barcode")]
    public string Barcode { get; init; } = string.Empty;

    [JsonPropertyName("locationId")]
    public Guid LocationId { get; init; }

    [JsonPropertyName("products")]
    public IReadOnlyList<DroppProduct> Products { get; init; } = [];

    [JsonPropertyName("customer")]
    public DroppCustomer? Customer { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset? CreatedAt { get; init; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset? UpdatedAt { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; init; }
}

public sealed record DroppDeliveryPostalCode(
    [property: JsonPropertyName("code")] short Code,
    [property: JsonPropertyName("town")] string Town,
    [property: JsonPropertyName("capital")] bool? Capital = null);

public sealed class DroppDeliveryPostalCodes
{
    [JsonPropertyName("status")]
    public int Status { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("codes")]
    public IReadOnlyList<DroppDeliveryPostalCode> Codes { get; init; } = [];

    [JsonPropertyName("flytjandicodes")]
    public IReadOnlyList<DroppDeliveryPostalCode> FlytjandiCodes { get; init; } = [];
}
