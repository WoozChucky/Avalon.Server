# ValueObject — boundaries and OpenAPI

`ValueObject<TPrimitive>` (in `Avalon.Common`) wraps primitives such as `AccountId`, `WorldId` and `CharacterId`.
**Value objects live inside the server and stop at every boundary**: each edge unwraps them explicitly rather than
relying on automatic serialization.

| Boundary | How |
|---|---|
| Database | EF `HasConversion` registrations in the three `DbContext`s |
| Protobuf wire | packet contracts declare primitives; handlers pass `.Value` |
| REST JSON | DTOs (`Avalon.Api.Contract`) declare primitives; mappers pass `.Value`. Enforced by `ApiContractShould` |

Nothing on a value object makes it serialize as its primitive by itself: no attribute, no global converter.
`Avalon.Common.Converters.ValueObjectJsonConverterFactory` does it, but only for a `JsonSerializerOptions` that
registers it, and its one caller is the item-catalog export (`tools/Avalon.Exporter`), which serializes
`ItemTemplate` entities directly. Serialized without it, a value object comes out as `{"value":42}`.

## OpenAPI

The API registers neither that converter nor an OpenAPI schema transformer (`AvalonApiHost` says where the converter
would go), because nothing on its surface is a value object, so the published OpenAPI document has nothing to
flatten. If a DTO ever needs to expose one, `ApiContractShould` fails and says so: that is the signal to register the
converter and add a schema transformer that describes the value object as its primitive, not to delete the test.
