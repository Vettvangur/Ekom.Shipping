# Ekom.Shipping.Dropp

Dropp checkout and fulfillment integration for Ekom. The package supports pickup
locations, home-delivery availability, shipment creation and management, labels,
extra packages, returns, and tracking.

## Installation

For Dropp with Ekom on Umbraco 17:

```bash
dotnet add package Ekom.Shipping.Dropp
dotnet add package Ekom.Shipping.U17
```

Use `Ekom.Shipping.U10` for Umbraco 13 or `Ekom.Shipping.U18` for Umbraco 18.
You do not need to install `Ekom.Shipping` or `Ekom.Shipping.Core` separately.
Then register Dropp with `services.AddDroppShipping(configuration)`.

For standalone Dropp API access without Ekom integration, install only
`Ekom.Shipping.Dropp`.

## Official Dropp API documentation

- [Dropp API documentation](https://documenter.getpostman.com/view/1057001/SzKPU13n?version=latest)
- Production API base URL: `https://api.dropp.is/dropp/api/v1/`
- Staging API base URL: `https://stage.dropp.is/dropp/api/v1/`
- Production access: [create a Dropp account](https://dropp.is/stofna-adgang)
- Staging credentials and store ID: contact `dropp@dropp.is`

Dropp uses `Authorization: Basic YOUR-TOKEN`. Treat the API key as a secret and
keep it in application configuration.

## Configuration

```json
{
  "Ekom": {
    "Shipping": {
      "Dropp": {
        "Accounts": {
          "dropp-main": {
            "ApiUrl": "https://api.dropp.is/dropp/api/v1/",
            "ApiKey": "use-a-secret-provider",
            "StoreId": "your-store-id",
            "FulfillmentMode": "Manual",
            "CacheDuration": "06:00:00"
          }
        }
      }
    }
  }
}
```

### Umbraco provider node

Set these properties on the Ekom shipping provider node:

| Property | Value |
|---|---|
| `shippingCarrierAlias` | `dropp` |
| `shippingCarrierAccount` | The configured account name, for example `dropp-main` |
| `shippingCarrierService` | `pickup` or `home-delivery` |

`FulfillmentMode` defaults to `Manual`. Set it to `Automatic` to create a
shipment during `CheckoutEvents.CompleteCheckoutAsync`.

## Public pickup locations

With an `Ekom.Shipping.U*` adapter installed, checkout UIs can load the
configured provider's cached Dropp locations without a custom controller:

```http
GET /ekom/shipping/providers/{providerKey}/pickup-locations?storeAlias=main&countryCode=IS&postalCode=101
```

`providerKey` is the Ekom shipping provider node key. Dropp locations use the
account's `CacheDuration`, which defaults to six hours.

## Supported API operations

Inject `IDroppShippingService` for direct access:

| Operation | Method |
|---|---|
| List pickup locations | `GetPickupLocationsAsync` |
| List delivery services for a destination | `GetServicesAsync` |
| Get home-delivery and Flytjandi postal codes | `GetDeliveryPostalCodesAsync` |
| Reserve an order barcode | `ReserveBookingReferenceAsync` |
| Create an Ekom-neutral shipment | `CreateShipmentAsync` |
| Create a complete Dropp order | `CreateOrderAsync` |
| Get a Dropp order | `GetOrderAsync` |
| Update an initial Dropp order | `UpdateOrderAsync` |
| Delete an order before collection | `DeleteOrderAsync` |
| Get a shipping label | `GetLabelAsync` |
| Get a return label | `GetReturnLabelAsync` |
| Create an extra-package barcode and label | `CreateExtraPackageLabelAsync` |
| Delete an extra order | `DeleteExtraOrderAsync` |
| Get tracking JSON | `GetTrackingAsync` |

```csharp
var order = await dropp.GetOrderAsync("dropp-main", droppOrderId, cancellationToken);

await dropp.UpdateOrderAsync(
    "dropp-main",
    droppOrderId,
    new DroppUpdateOrderRequest(Barcode: "ORDER-NEW"),
    cancellationToken);

await dropp.DeleteOrderAsync("dropp-main", droppOrderId, cancellationToken);
```

For advanced creation, `DroppCreateOrderRequest` exposes `DayDelivery`,
`Comment`, and `ReturnOrder`. Email address and social-security number are
optional; name, address, phone number, postal code, town, location, value, and
at least one valid product are validated before submission.

`DroppShippingDefaults.HomeDeliveryLocationId` and
`DroppShippingDefaults.SamskipLocationId` expose the fixed location IDs listed
in Dropp's documentation.

## Ekom order management

`IShippingFulfillmentService` keeps Ekom fulfillment state synchronized and is
preferred when managing a shipment belonging to an Ekom order. Created
shipments expose these order-manager actions:

- **Print shipping label**
- **View shipment JSON**
- **Delete shipment**

Deletion first checks the current Dropp status. Dropp only allows deletion
before collection while the order is `initial`. A confirmed deletion is retained
as a tombstone on the Ekom order and is not automatically recreated.

Direct calls to update or delete a Dropp order bypass Ekom state management.
Use them only when the application also owns the corresponding synchronization.

## Mutation safety

Dropp does not document POST, PATCH, DELETE, extra-package, or extra-order
operations as idempotent. Network failures and ambiguous server responses throw
`ShipmentOutcomeUnknownException`; do not blindly retry them. The built-in Ekom
orchestration records uncertain creation and deletion outcomes in the order's
`ShippingProvider.CustomData`.

The built-in per-order lock is process-local. Multi-node sites must provide a
distributed order lock before enabling automatic fulfillment.
