# Ekom.Shipping.IcelandicPost

Pósturinn checkout and fulfillment integration for Ekom. The package supports
delivery-service pricing, pickup locations, shipment creation and management,
labels, tracking, proof of delivery, and PrintNode printing.

## Official API documentation

- [Pósturinn Swagger UI](https://api.mobiz.posturinn.is/index.html)
- [OpenAPI 3 specification](https://api.mobiz.posturinn.is/wscm_v1.yaml)
- Production base URL: `https://api.mobiz.posturinn.is/api/wscm/`
- Test base URL: `https://api.mobiz.test.posturinn.is/api/wscm/`
- Live API keys: [Fyrirtækjasíður](https://fyrirtaeki.posturinn.is)

The test environment is documented as available on weekdays from 07:00–19:00.
Keys are environment-specific. Authentication uses the `x-api-key` header; keep
the key in application configuration.

## Configuration

```json
{
  "Ekom": {
    "Shipping": {
      "IcelandicPost": {
        "Accounts": {
          "post-main": {
            "ApiUrl": "https://api.mobiz.posturinn.is/api/wscm/",
            "ApiKey": "use-a-secret-provider",
            "FulfillmentMode": "Manual",
            "LabelFormat": "Unknown",
            "CacheDuration": "03:00:00"
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
| `shippingCarrierAlias` | `icelandic-post` |
| `shippingCarrierAccount` | The configured account name, for example `post-main` |
| `shippingCarrierService` | A delivery-service ID returned by Pósturinn |

Common service IDs are:

| Service ID | Delivery type |
|---|---|
| `DPH` | Normal address delivery used by the current integration examples |
| `DPO` | Póstbox selection |
| `DNO` | Alternative Póstbox service when returned by Pósturinn |
| `DPT` | Parcel point selection |
| `DPP` | Post office selection |

Most sites only need normal address delivery and Póstbox delivery. Configure
separate Ekom shipping provider nodes for them.

Normal address delivery:

```text
shippingCarrierAlias: icelandic-post
shippingCarrierAccount: post-main
shippingCarrierService: DPH
```

Póstbox selection:

```text
shippingCarrierAlias: icelandic-post
shippingCarrierAccount: post-main
shippingCarrierService: DPO
```

Confirm that Pósturinn returns the configured service for the destination and
account. `DPO` enables Póstbox selection at checkout, but built-in fulfillment
cannot book a generic pickup ID. Booking requires the carrier-qualified service
ID returned for the selected Póstbox, and that mapping is not automatic.

Register with `services.AddIcelandicPostShipping(configuration)`.

`FulfillmentMode` defaults to `Manual`. Set it to `Automatic` to create a
shipment from `CheckoutEvents.CompleteCheckoutAsync`.

## Supported API operations

Inject `IIcelandicPostShippingService` for direct API access:

| Operation | Method |
|---|---|
| Rich delivery services and prices | `GetDeliveryServicesAsync` |
| Provider-neutral delivery services | `GetServicesAsync` |
| Postboxes | `GetPostboxesAsync` |
| Parcel points | `GetParcelPointsAsync` |
| Post offices | `GetPostOfficesAsync` |
| Create a shipment | `CreateShipmentAsync` |
| Get a shipment and tracking history | `GetShipmentAsync` |
| Get current status | `GetShipmentStatusAsync` |
| Delete a shipment | `DeleteShipmentAsync` |
| Find shipments by reference | `GetShipmentsByReferenceAsync` |
| List shipments by date range | `GetShipmentsAsync` |
| Get one PDF label | `GetLabelAsync` |
| Get a combined labels PDF | `GetCombinedLabelsAsync` |
| Get proof-of-delivery JPEG | `GetProofOfDeliveryAsync` |
| Print through PrintNode | `PrintAsync` |
| Provider-neutral tracking JSON | `GetTrackingAsync` |

```csharp
var shipment = await post.GetShipmentAsync(
    "post-main",
    "CF083141763IS",
    "IS",
    cancellationToken);

var labels = await post.GetCombinedLabelsAsync(
    "post-main",
    ["CF083141763IS", "CF083141764IS"],
    IcelandicPostLabelFormat.A4Size,
    cancellationToken);

await post.DeleteShipmentAsync(
    "post-main",
    shipment.ShipmentId,
    cancellationToken: cancellationToken);
```

`IcelandicPostShipmentRequest` exposes parcels, customs contents, recipient
details, return permissions, dimensions, insurance, and other commonly used
shipment options. Verify that optional services are supported by the selected
delivery service before setting them.

## Pickup services

- Qualified IDs such as `DPO1013` and `DPP200` identify a carrier-selected
  destination and are preserved as returned.

The API does not document a reliable conversion from location IDs such as
`IS101A` to qualified delivery-service IDs. The built-in fulfillment mapper
therefore rejects generic pickup service IDs instead of inventing a mapping.

## Ekom fulfillment

The built-in Ekom mapper intentionally supports **domestic, single-parcel
shipments only**. It does not map purchased order lines to parcels. International
automatic fulfillment is rejected because it requires explicit customs content,
tariff codes, origin countries, and customs values.

Created shipments use the shared order-manager actions:

- **Print shipping label**
- **View shipment JSON**
- **Delete shipment**

These actions go through `IShippingFulfillmentService`, keeping state in the
order's `ShippingProvider.CustomData`. Confirmed deletion is retained and is not
automatically recreated.

## Mutation safety

Pósturinn does not document creation, deletion, or PrintNode calls as idempotent.
Ambiguous transport and server outcomes throw `ShipmentOutcomeUnknownException`.
Do not blindly retry them. A merchant reference can return multiple parcel and
return IDs, so it is useful for reconciliation but is not treated as an
idempotency key.

The built-in per-order lock is process-local. Multi-node sites must provide a
distributed order lock before enabling automatic fulfillment.
