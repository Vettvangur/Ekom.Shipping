# Ekom.Shipping.DhlLocationFinder

Typed client for DHL Location Finder Unified API 1.14.0.

Register with `services.AddDhlLocationFinder(configuration)` and configure
accounts under `Ekom:Shipping:DhlLocationFinder:Accounts`. Each account requires
an HTTPS `ApiUrl` (normally `https://api.dhl.com/location-finder/v1/`) and an
`ApiKey` sent through the `DHL-API-Key` header.

```json
{
  "Ekom": {
    "Shipping": {
      "DhlLocationFinder": {
        "Accounts": {
          "dhl-locations": {
            "ApiUrl": "https://api.dhl.com/location-finder/v1/",
            "ApiKey": "use-a-secret-provider"
          }
        }
      }
    }
  }
}
```

The client supports address, coordinate, keyword-ID, and location-ID lookups.
It does not cache responses. DHL's published terms prohibit storing or modifying
Location Data without permission. Obtain appropriate approval before persisting
even a selected location snapshot.

The published terms also impose display conditions when DHL locations appear
beside other logistics providers, including showing all returned DHL locations
and avoiding selective recommendations contrary to DHL's interests. Review the
current terms for your application before exposing lookup results.

Unified Location Finder identifiers are not documented as MyDHL Express
On-Demand Delivery service-point identifiers. `ToPickupLocation` is a transient
display mapping only and does not establish shipment eligibility.

Official documentation:
https://developer.dhl.com/api-reference/location-finder-unified
