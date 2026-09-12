# Ekom.Shipping

Reusable checkout and fulfillment providers for Ekom. Checkout selection,
automatic carrier booking, manual booking, and label printing use the same
provider services. Shipment state is stored on the order's shipping-provider
`CustomData`; the package creates no shipping table.

## Packages

- `Ekom.Shipping.Core` — provider-neutral contracts and validation.
- `Ekom.Shipping` — Ekom shipping-method and order-data integration.
- [`Ekom.Shipping.DhlExpress`](src/Providers/Ekom.Shipping.DhlExpress/README.md) — MyDHL rates, international fulfillment, creation-time labels, tracking, documents, and pickups.
- [`Ekom.Shipping.DhlLocationFinder`](src/Providers/Ekom.Shipping.DhlLocationFinder/README.md) — transient DHL Unified location searches.
- [`Ekom.Shipping.Dropp`](src/Providers/Ekom.Shipping.Dropp/README.md) — Dropp locations, booking, order management, labels, returns, extra packages, and tracking.
- [`Ekom.Shipping.IcelandicPost`](src/Providers/Ekom.Shipping.IcelandicPost/README.md) — Pósturinn services, pickup locations, booking, labels, tracking, and shipment management.
- `Ekom.Shipping.U10` — Umbraco 13 integration.
- `Ekom.Shipping.U17` — Umbraco 17 integration.
- `Ekom.Shipping.U18` — Umbraco 18 integration.

## Configuration

Credentials remain in application configuration. Backoffice shipping methods
store only a provider alias, account reference, and service ID.

```json
{
  "Ekom": {
    "Shipping": {
      "Dropp": {
        "Accounts": {
          "dropp-main": {
            "ApiUrl": "https://api.example/",
            "ApiKey": "use-a-secret-provider",
            "StoreId": "store-id",
            "FulfillmentMode": "Automatic"
          }
        }
      },
      "DhlExpress": {
        "Accounts": {
          "dhl-main": {
            "ApiUrl": "https://express.api.dhl.com/mydhlapi/",
            "ApiVersion": "3.3.2",
            "Username": "use-a-secret-provider",
            "Password": "use-a-secret-provider",
            "AccountNumber": "billing-account",
            "ProductCode": "P",
            "ProductName": "DHL Express",
            "FulfillmentMode": "Manual",
            "Shipper": {
              "Name": "Warehouse contact",
              "CompanyName": "Merchant",
              "Phone": "phone",
              "Email": "shipping@example.com",
              "AddressLine1": "Origin address",
              "PostalCode": "101",
              "CityName": "Reykjavík",
              "CountryCode": "IS"
            }
          }
        }
      },
      "DhlLocationFinder": {
        "Accounts": {
          "dhl-locations": {
            "ApiUrl": "https://api.dhl.com/location-finder/v1/",
            "ApiKey": "use-a-secret-provider"
          }
        }
      },
      "IcelandicPost": {
        "Accounts": {
          "post-main": {
            "ApiUrl": "https://api.mobiz.posturinn.is/api/wscm/",
            "ApiKey": "use-a-secret-provider",
            "FulfillmentMode": "Manual"
          }
        }
      }
    }
  }
}
```

Register only the carrier packages used by the site:

```csharp
services.AddEkomShipping();
services.AddDhlExpressShipping(configuration);
services.AddDhlLocationFinder(configuration);
services.AddDroppShipping(configuration);
services.AddIcelandicPostShipping(configuration);
```

DHL Express Ekom fulfillment additionally requires one
`IDhlExpressShipmentEnricher` and one private `IShippingDocumentStore`.
The enricher supplies measured parcels, planned origin time, and international
customs data. The document store durably saves creation-time labels while only
opaque references are retained in `ShippingProvider.CustomData`.
The store must treat repeated writes to the supplied deterministic references as
idempotent and define private access control, retention, and order-cleanup rules.

## Ekom shipping-provider properties

Add these non-secret properties to the `ekmShippingProvider` document type and
manage their values in the Umbraco backoffice:

| Alias | Meaning | Example |
|---|---|---|
| `shippingCarrierAlias` | Registered carrier | `dropp` |
| `shippingCarrierAccount` | Account configuration reference | `dropp-main` |
| `shippingCarrierService` | Carrier service | `pickup` |

Legacy shipping providers with all three properties empty continue to work as
normal Ekom shipping providers. Partially configured providers fail validation.

When an account's `FulfillmentMode` is `Automatic`, the Umbraco adapter creates
the shipment from `CheckoutEvents.CompleteCheckoutAsync`. Accounts default to
`Manual` when the setting is omitted. Automatic fulfillment does not depend on
the order becoming `ReadyForDispatch`, so offline-payment and customized order
status flows are supported. Carrier failures are logged and saved on the order;
they do not fail checkout completion.

The Umbraco packages register the shared checkout and fulfillment services. An
automatic document-type migration and a provider-aware backoffice selector are
planned before the first stable release.

## Checkout usage

Resolve `IEkomShippingCheckoutService`, pass it the selected Ekom shipping
provider and destination, then use `GetServicesAsync`,
`GetPickupLocationsAsync`, and `ValidateAsync`. `SaveSelectionAsync` validates
the selection and passes a normalized dictionary to Ekom's existing
`UpdateShippingInformationAsync` method. This ensures names and addresses saved
with the order come from the carrier rather than the browser.

Sites remain responsible for checkout HTML, maps, CSP rules, antiforgery, and
cart ownership checks.

## Fulfillment

`IShippingFulfillmentService` is the recommended Ekom-level API:

```csharp
await fulfillment.CreateAsync(orderId, cancellationToken);
await fulfillment.RetryAsync(orderId, cancellationToken);
var label = await fulfillment.GetLabelAsync(orderId, cancellationToken);
```

`CreateAsync` works regardless of the configured automatic/manual mode.
Automatic checkout processing calls the same implementation. Sites can add
`IShippingAutomationRule` implementations to veto automatic creation, and can
replace `IShippingOrderMapper` when their order-to-carrier mapping differs.

For provider-specific manual usage, inject `IDroppShippingService`. It exposes
location lookup, barcode reservation, booking, PDF labels, and tracking without
requiring the Ekom fulfillment orchestration service.

The Ekom order manager receives context-sensitive actions automatically:

- **Create shipment** when no shipment exists.
- **Retry shipment** after a definite failure.
- **Print shipping label** after successful creation.
- **View shipment JSON** to fetch current carrier details.
- **Delete shipment** when the carrier supports deletion and the shipment has not progressed too far.
- A disabled warning when the outcome is unknown.

Printing never creates a shipment implicitly.

Confirmed deletions remain recorded on the order and are not automatically
recreated. An uncertain deletion is not blindly retried.

### Order custom data

The orchestration service stores these values under `ShippingProvider.CustomData`:

- `customshippingShipmentState`
- `customshippingShipmentAttempts`
- `customshippingShipmentReference`
- `customshippingShipmentId`
- `customshippingTrackingNumber`
- `customshippingShipmentLastError`
- `customshippingShipmentUpdatedUtc`
- `customshippingCarrierAlias`
- `customshippingAccountReference`
- `customshippingServiceId`

Existing Dropp values (`customshippingDroppBarcode` and
`customshippingDroppOrderId`) are recognized for label compatibility.

State is written before carrier submission. A connection failure after
submission becomes `OutcomeUnknown` and is not blindly retried, because Dropp's
idempotency behavior has not been confirmed. Per-order semaphores prevent
duplicate submissions within one application process. Multi-node sites should
add a distributed order lock before enabling automatic creation.
