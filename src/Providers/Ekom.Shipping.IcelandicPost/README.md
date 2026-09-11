# Ekom.Shipping.IcelandicPost

Íslandspóstur checkout lookup integration for Ekom. The current package lists
available delivery services and postboxes. It does not create shipments, print
labels, track parcels, or perform fulfillment.

## Íslandspóstur API access

- [Pósturinn business web solutions](https://posturinn.is/fyrirtaeki/veflausnir/)
- [Pósturinn website](https://posturinn.is/)

A public, versioned machine-readable specification for the endpoints used by
this package has not been verified. Request current API documentation,
credentials, and environment URLs directly from Pósturinn before integrating.

The API uses an `x-api-key` header. Keep the key in application configuration.

## Configuration

```json
{
  "Ekom": {
    "Shipping": {
      "IcelandicPost": {
        "Accounts": {
          "post-main": {
            "ApiUrl": "https://your-posturinn-api-base/",
            "ApiKey": "use-a-secret-provider",
            "CacheDuration": "03:00:00"
          }
        }
      }
    }
  }
}
```

Register the package with `services.AddIcelandicPostShipping(configuration)`.
Set the Ekom shipping provider carrier alias to `icelandic-post`, its account to
the configured account name, and its service to the required Pósturinn service
ID. `DPO` is treated as the postbox service and requires a selected postbox.

## Supported operations

Inject `IIcelandicPostShippingService`:

```csharp
var services = await post.GetServicesAsync(
    "post-main",
    new ShippingLookupRequest("store", "IS", "101"),
    cancellationToken);

var postboxes = await post.GetPickupLocationsAsync(
    "post-main",
    IcelandicPostShippingDefaults.PostboxServiceId,
    new ShippingLookupRequest("store", "IS", "101"),
    cancellationToken);
```

The package currently calls the delivery-services-and-prices and postbox lookup
endpoints. Prices returned by the former are not currently exposed in the
provider-neutral `ShippingService` model.
