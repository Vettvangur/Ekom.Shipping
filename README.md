# Ekom.Shipping

Reusable checkout shipping providers for Ekom. The first release focuses on
service and pickup-location lookup, server-side selection validation, and a
consistent Ekom order-data format. It does not create carrier shipments or
labels.

## Packages

- `Ekom.Shipping.Core` — provider-neutral contracts and validation.
- `Ekom.Shipping` — Ekom shipping-method and order-data integration.
- `Ekom.Shipping.Dropp` — Dropp pickup locations.
- `Ekom.Shipping.IcelandicPost` — Íslandspóstur services and postboxes.
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
            "StoreId": "store-id"
          }
        }
      },
      "IcelandicPost": {
        "Accounts": {
          "post-main": {
            "ApiUrl": "https://api.example/",
            "ApiKey": "use-a-secret-provider"
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
services.AddDroppShipping(configuration);
services.AddIcelandicPostShipping(configuration);
```

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

The Umbraco packages currently register the shared Ekom checkout services. An
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
