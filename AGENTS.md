# Agent Notes

## Commands

- Restore: `dotnet restore Ekom.Shipping.slnx`
- Build: `dotnet build Ekom.Shipping.slnx --no-restore`
- Test: `dotnet test tests/Ekom.Shipping.Tests/Ekom.Shipping.Tests.csproj`

The full solution requires .NET 10 because the Umbraco 17 and 18 adapters target
`net10.0`. Shared and carrier projects target `net8.0` and can be consumed by
.NET 8 and newer applications.

## Conventions

- Use explicit DI registration and stable, case-insensitive carrier aliases.
- Keep credentials in application configuration, never Umbraco content.
- Store fulfillment state in `ShippingProvider.CustomData`; do not add a shipping table.
- Automatic fulfillment runs from `CheckoutEvents.CompleteCheckoutAsync` and is independent of order status.
- Manual actions and automatic processing must use `IShippingFulfillmentService`.
- Carrier calls must accept cancellation and must not log secrets or PII.
- Persist only server-validated carrier snapshots under `customshipping*` keys.
- Use LF line endings.
