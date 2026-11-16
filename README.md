# Ekom.Shipping

Shipping carriers for Ekom. Each package connects one carrier to Ekom's shipping
framework: services and pickup points at checkout, automatic or manual booking,
labels, and shipment management from the order manager.

The framework itself is part of Ekom 1.3 and later: the carrier contracts,
checkout selection, shipment booking, and the record of every shipment and what
happened to it. See Ekom's `docs/guides/shipping.md`.

## Packages

- [`Ekom.Shipping.DhlExpress`](src/Providers/Ekom.Shipping.DhlExpress/README.md) — MyDHL rates, international fulfillment, creation-time labels, tracking, documents, and pickups.
- [`Ekom.Shipping.DhlLocationFinder`](src/Providers/Ekom.Shipping.DhlLocationFinder/README.md) — transient DHL Unified location searches.
- [`Ekom.Shipping.Dropp`](src/Providers/Ekom.Shipping.Dropp/README.md) — Dropp locations, booking, order management, labels, returns, extra packages, and tracking.
- [`Ekom.Shipping.IcelandicPost`](src/Providers/Ekom.Shipping.IcelandicPost/README.md) — Pósturinn services, pickup locations, booking, labels, tracking, and shipment management.

## Installation

Install the package of each carrier the site uses. They require Ekom 1.3 or
later and work with every Umbraco version Ekom supports; there is no separate
package per Umbraco version.

### Upgrading from 0.x

Before 1.0 the framework was its own packages. Remove `Ekom.Shipping`,
`Ekom.Shipping.Core` and `Ekom.Shipping.U10`/`U17`/`U18`, update Ekom to 1.3 or
later and the carrier packages to 1.0, and remove the `AddEkomShipping()` call.
Types from the `Ekom.Shipping.Ekom` namespace are in `Ekom.Shipping`, and
`ShippingFulfillmentState` is `Ekom.Models.Shipments.ShipmentState`.

Shipments are no longer kept on the order's shipping custom data. Ekom copies
the shipment of an existing order to its shipment records the first time the
order is opened in the manager or its shipment is read.

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
services.AddDhlExpressShipping(configuration);
services.AddDhlLocationFinder(configuration);
services.AddDroppShipping(configuration);
services.AddIcelandicPostShipping(configuration);
```

DHL Express Ekom fulfillment additionally requires one
`IDhlExpressShipmentEnricher` and one private `IShippingDocumentStore`.
The enricher supplies measured parcels, planned origin time, and international
customs data. The document store durably saves creation-time labels; Ekom's
shipment record keeps only opaque references to them.
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

When an account's `FulfillmentMode` is `Automatic`, Ekom creates
the shipment when checkout completes. Accounts default to
`Manual` when the setting is omitted. Automatic fulfillment does not depend on
the order becoming `ReadyForDispatch`, so offline-payment and customized order
status flows are supported. Carrier failures are logged and recorded on the
shipment; they do not fail checkout completion.

### Tracking links

Set `TrackingUrlTemplate` on a carrier account to the carrier's public tracking
address, with `{trackingNumber}` in place of the number:

```json
"dropp-main": {
  "TrackingUrlTemplate": "https://tracking.example/?number={trackingNumber}"
}
```

Ekom stores the link on the shipment, and the order manager shows it next to
the tracking number.

## Checkout usage

Resolve `IEkomShippingCheckoutService`, pass it the selected Ekom shipping
provider and destination, then use `GetServicesAsync`,
`GetPickupLocationsAsync`, and `ValidateAsync`. `SaveSelectionAsync` validates
the selection and passes a normalized dictionary to Ekom's existing
`UpdateShippingInformationAsync` method. This ensures names and addresses saved
with the order come from the carrier rather than the browser.

Sites remain responsible for checkout HTML, maps, CSP rules, antiforgery, and
cart ownership checks.

### Public pickup locations

Ekom exposes a public read-only endpoint for checkout UIs:

```http
GET /ekom/shipping/providers/{providerKey}/pickup-locations?storeAlias=main&countryCode=IS&postalCode=101
```

`providerKey` is the key of the Ekom shipping provider node. The endpoint uses
the carrier, account, and service configured on that node and returns normalized
location IDs, names, addresses, postal codes, cities, and coordinates. A valid
provider with no matching locations returns an empty JSON array.

Dropp location data uses its configured cache duration, which defaults to six
hours. Icelandic Post postboxes, parcel points, and post offices default to a
three-hour cache. The selected location is still validated with the carrier
when checkout saves it. Production sites should include this public endpoint in
their normal ASP.NET Core or edge rate-limiting policy.

## Fulfillment

`IShippingFulfillmentService`, in Ekom, books, retries, prints and deletes the
carrier shipment of an order.

The Ekom order manager receives context-sensitive actions automatically:

- **Create shipment** when no shipment exists.
- **Retry shipment** after a definite failure.
- **Print shipping label** after successful creation.
- **View shipment JSON** to fetch current carrier details.
- **Delete shipment** when the carrier supports deletion and the shipment has not progressed too far.
- **Create new shipment** after a shipment was deleted.
- A disabled warning when the outcome is unknown.

Printing never creates a shipment implicitly.

A deleted shipment stays in the order's shipment history. An uncertain deletion
is not blindly retried.

Each step is recorded before the carrier is called. A connection failure after
submission becomes `OutcomeUnknown` and is not blindly retried, because Dropp's
idempotency behavior has not been confirmed. Per-order semaphores prevent
duplicate submissions within one application process. Multi-node sites should
add a distributed order lock before enabling automatic creation.

## Releases and NuGet publishing

GitHub Actions builds, tests, and packs all projects on pull requests and pushes
to `main`. Release Please maintains four independently versioned components:

| Component | Packages | Tag format |
|---|---|---|
| DHL Express | DHL Express provider | `Ekom.Shipping.DhlExpress-v1.0.0` |
| DHL Location Finder | Location Finder client | `Ekom.Shipping.DhlLocationFinder-v1.0.0` |
| Dropp | Dropp provider | `Ekom.Shipping.Dropp-v1.0.0` |
| Icelandic Post | Icelandic Post provider | `Ekom.Shipping.IcelandicPost-v1.0.0` |

Conventional commits under a component's path update only that component's
release PR and changelog. Each release group has an isolated Release Please
manifest under `.github/release-please`, so provider release PRs do not modify a
shared version file or conflict with each other. Merging a Release Please PR
creates its component tag; the tag workflow rebuilds and tests the full
solution, packs only that release group, verifies package and symbol files, and
publishes them to NuGet.org.

Repository setup required for releases:

1. Add the `RELEASE_PLEASE_TOKEN` repository secret, matching the Ekom
   repository setup. The token must be able to create release PRs, releases, and
   tags so tag workflows are triggered.
2. Configure NuGet.org trusted publishing for every package, using owner
   `Vettvangur`, repository `Ekom.Shipping`, and workflow
   `publish-ekom-shipping.yml`.
3. Ensure the `Vettvangur` NuGet organization owns each package ID. New package
   IDs may require an initial ownership or trusted-publishing setup step.

Publishing can be rerun manually by supplying an existing component release tag
to the workflow. Arbitrary branch versions are rejected, and the tag version
must match the component version checked into that tag.
