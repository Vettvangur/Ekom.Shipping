# Ekom.Shipping.DhlExpress

DHL Express MyDHL API 3.3.2 integration for Ekom Shipping. It provides typed
rates, products, address capability, international shipment creation, tracking,
proof-of-delivery, document-image, invoice/image upload, and courier-pickup APIs.

## Configuration

Configure accounts under `Ekom:Shipping:DhlExpress:Accounts` with `ApiUrl`,
`ApiVersion`, `Username`, `Password`, `AccountNumber`, `ProductCode`,
`ProductName`, `FulfillmentMode`, and one default `Shipper`. The production URL
is `https://express.api.dhl.com/mydhlapi/`; the test URL is
`https://express.api.dhl.com/mydhlapi/test/`.

```json
{
  "Ekom": {
    "Shipping": {
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
      }
    }
  }
}
```

Register with `services.AddDhlExpressShipping(configuration)`. Credentials use
pre-emptive HTTP Basic authentication and the configured version is sent in the
required `x-version` header.

Inject `IDhlExpressShippingService` for typed access to:

- Multi-piece rates and one-piece products
- Pickup/delivery address capability validation
- Shipment validation and creation
- Detailed tracking
- Proof of delivery and supported document images
- Paperless Trade image and invoice-data uploads
- Courier pickup creation, update, and cancellation

## Ekom fulfillment

Register exactly one `IDhlExpressShipmentEnricher`. It receives the configured
shipper and the provider-neutral order request and must return measured physical
packages, planned origin time, receiver details, and international customs data.
Purchased lines are not treated as parcels. Customs-declarable shipments require
declared value, currency, export lines, commodity/manufacturing data, and a net
or gross weight per line.

DHL transport labels are returned during creation and are not generally
downloadable later. Register exactly one private `IShippingDocumentStore` when
using Ekom fulfillment. The fulfillment service stores labels and document
metadata before marking the shipment Created; only opaque references are kept
in `ShippingProvider.CustomData`.

`StoreAsync` receives deterministic references and must be idempotent and
all-or-nothing for each request. The implementation is responsible for private
authorization, encryption where appropriate, retention, and cleanup when an
order is removed. If persistence is interrupted after DHL creation, fulfillment
remains non-retryable and the same references can be used for reconciliation.

The carrier does not expose shipment deletion. `CancelPickupAsync` only cancels
a courier pickup and must not be interpreted as deleting or voiding an AWB.
Unified Location Finder IDs are not mapped to Express destination service-point
IDs.

DHL's published terms restrict storing, modifying, and disclosing Product and
Rating Data. This package does not cache rates or product responses. Confirm the
terms applicable to your DHL agreement before persisting or redistributing them.

Official documentation:
https://developer.dhl.com/api-reference/dhl-express-mydhl-api
