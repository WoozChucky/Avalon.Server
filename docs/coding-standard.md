# Coding standard

The C# in this repository follows one standard: Microsoft's
[C# coding conventions](https://learn.microsoft.com/dotnet/csharp/fundamentals/coding-style/coding-conventions) and
[identifier naming rules](https://learn.microsoft.com/dotnet/csharp/fundamentals/coding-style/identifier-names), in the
form the [dotnet/runtime coding style](https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/coding-style.md)
gives them. The root `.editorconfig` encodes it, and the rules below are enforced: every build reports a violation as a
warning, and CI fails on it (three file-level details, noted under Layout, are left to `dotnet format`). Everything else
`.editorconfig` mentions is a hint the IDE may offer and nobody has to take.

## The rules

### Layout (IDE0055, IDE2000)

Four spaces of indentation, braces on their own lines (Allman), one statement per line, a space after keywords and
commas and around binary operators, no trailing whitespace, and no more than one blank line in a row (IDE2000). Every
file is UTF-8 without a byte-order mark and ends with a newline, and its using directives are sorted; the build checks
none of those three, `dotnet format` fixes and verifies them (see below). Line endings are left as Git stores them.

```csharp
if (target is null)
{
    return;
}

foreach (ICreature creature in creatures)
{
    Tick(creature);
}
```

### Namespaces and using directives (IDE0161, IDE0065, IDE0005)

One file-scoped namespace per file (IDE0161). Using directives go above it (IDE0065), sorted, `System` first, with
none the file does not need (IDE0005). Implicit usings stay on, so `System`, `System.Linq` and the like rarely appear.

```csharp
using Avalon.Common.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Parties;
```

### Braces (IDE0011)

A body that spans more than one line takes braces (`csharp_prefer_braces = when_multiline`). A one-line body under a
one-line statement may go without, and an `if`/`else` chain braces every branch once one branch needs them.

```csharp
if (party is null)
    return PartyResult.NotInParty;

if (attacker is CharacterEntity attackerEntity
    && target is CharacterEntity { IsDead: false } targetEntity)
{
    _pvp?.OnPlayerHitPlayer(attackerEntity, targetEntity);
}
```

### Naming (IDE1006)

| what | style | example |
| --- | --- | --- |
| types, methods, local functions, properties, events, namespaces | PascalCase | `PartyService`, `TryPickUp` |
| interfaces | `I` + PascalCase | `IWorldConnection` |
| type parameters | `T` + PascalCase | `TPacket`, `T` |
| constants, fields and locals alike, enum members included | PascalCase | `const int MaxPartySize = 6;` |
| public fields | PascalCase | `public static NetworkPacketType PacketType` |
| private and internal instance fields | `_camelCase` | `private readonly ILogger _logger;` |
| private and internal static fields, `static readonly` included | `s_camelCase` | `private static readonly TimeSpan s_interval` |
| `[ThreadStatic]` fields | `t_camelCase` | `[ThreadStatic] private static MemoryStream? t_stream;` |
| parameters and locals | camelCase | `CharacterId characterId` |

A naming rule cannot select `[ThreadStatic]`, so the static-field rule asks for `s_` there too: a `t_` field carries a
targeted suppression (see below). The `Async` suffix is a convention for new code; it is not enforced and existing
methods were not renamed for it. Protected fields have no rule.

Renaming is the one fix tooling cannot apply in bulk safely: before renaming a member, look for its name as a string
(reflection, EF Core configuration, serializers) and remember that scripts and packet handlers are found by name or
attribute at run time (see [Packet handlers](packet-handlers.md)). Rename through the IDE so every reference follows.

### Types: keywords and `var` (IDE0049, IDE0007, IDE0008)

Language keywords, not BCL type names (IDE0049): `int`, `string`, `string.Empty`, `int.MaxValue`. `var` only where the
initializer shows the type (IDE0007): a `new`, a cast, an `as`, a `default(T)`, or a conversion such as `ToList()`;
everywhere else, and always for built-in types, the explicit type (IDE0008).

```csharp
var spillover = (Encounter)_registry.CreateEncounter();
var fighting = enc.Hostiles.Where(h => !IsReturningHome(h)).ToList();
CharacterClass healerClass = (healer as ICharacter)?.Class ?? CharacterClass.Healer;
int count = members.Count;
```

### `this.`, accessibility and modifier order (IDE0003, IDE0009, IDE0040, IDE0036)

No `this.` qualification unless a local or parameter shadows the member (IDE0003, IDE0009). Every member that is not an
interface member states its accessibility, `private` and `internal` included, and an interface member leaves out the
`public` it has anyway (IDE0040). Modifiers come in the order `public, private, protected, internal, file, static,
extern, new, virtual, abstract, sealed, override, readonly, unsafe, required, volatile, async` (IDE0036).

```csharp
private static double TicksToUs(long t) => t * s_usPerTick;
```

### No file headers

Source files carry no license header; the root `LICENSE` covers the repository, and `.editorconfig` sets no
`file_header_template`.

### Not enforced

Expression-bodied members, pattern matching, collection expressions, primary constructors, `readonly` fields, object
and collection initializers and the other simplifications stay at suggestion or silent. Their fixers can change
semantics or allocations, so they are neither enforced nor applied in bulk; take a suggestion where it reads better,
and keep game-server hot paths (the tick, packet handling, replication) free of allocation changes.

## How it is enforced

- `.editorconfig` sets the rules above to `warning`.
- The root `Directory.Build.props`, which `src/Directory.Build.props` imports and `tests/` and `tools/` reach directly,
  sets `EnforceCodeStyleInBuild`, so the build runs the code-style analyzers, and `GenerateDocumentationFile`, which
  IDE0005 needs in the build. The documentation file also makes the compiler check the XML doc comments that exist
  (well-formed XML, `<param>` names, resolvable `cref`s); a member or parameter without documentation is fine (CS1591
  and CS1573 are off). The file is not published, and the OpenAPI package's XML-comment generator is taken out of the
  build so the comments do not reach the OpenAPI document `Avalon.Api` serves.
- When `CI` is `true` (GitHub Actions sets it) the same file turns on `TreatWarningsAsErrors`, so any warning, code
  style or compiler or analyzer, fails the build. Locally the warnings are only warnings.
- Meziantou.Analyzer runs on `src/` (`src/Directory.Build.props` adds it). Its CancellationToken rules (MA0032, MA0040,
  MA0045) are errors in CI as before. Its opinion and design rules (file names, collection abstractions,
  `string.Equals`, method length and the like) are off in `.editorconfig`: they are not part of this standard. Its
  correctness rules stay on and, like any other warning, fail the CI build (#793): an explicit comparer or culture
  wherever strings are compared, sorted, hashed, formatted or parsed (MA0002, MA0011, MA0074; ordinal and the
  invariant culture unless the code means otherwise), a match timeout on every regex (MA0009,
  `matchTimeoutMilliseconds: 1000` on a `[GeneratedRegex]`), the caught exception kept as the inner one (MA0054), a
  completed task rather than null from a method that returns one (MA0022), an override's default values kept
  (MA0061), no implicit conversion to `DateTimeOffset` (MA0132), and every task either awaited or discarded on purpose
  with `_ =` and a comment saying what observes it (MA0134).

## Fixing violations locally

Build to see them (`dotnet build`, or `CI=true dotnet build` to see the build CI runs). Most fix themselves:

```bash
# Formatting, final newlines, byte-order marks and using order
dotnet format whitespace Avalon.sln
# The enforced code-style rules (everything above but naming)
dotnet format style Avalon.sln
# One rule at a time
dotnet format style Avalon.sln --diagnostics IDE0008
# Check without changing anything, the byte-order mark, final newline and using order included
dotnet format whitespace Avalon.sln --verify-no-changes
dotnet format style Avalon.sln --verify-no-changes
```

`tools/Avalon.Commerce.Check` and `tools/Avalon.EmailVerification.Check` are not in the solution: pass their `.csproj`
instead. Run `whitespace` and `style` rather than a bare `dotnet format`, whose `analyzers` pass would also apply
third-party fixes (Meziantou's among them) that can change behaviour. `dotnet format` does not fix naming (IDE1006):
rename in the IDE.

## When a rule cannot be met

A fix that would change behaviour is not made. The warning gets a suppression around the smallest span that holds
it, with the reason beside it, never a project-wide `NoWarn` to reach zero:

```csharp
// Keeps its name: tools/Avalon.Exporter's NavmeshVectors reads this field by name through reflection.
#pragma warning disable IDE1006
private static readonly RcVec3f PolyPickExt = new(2, 4, 2);
#pragma warning restore IDE1006
```

The exceptions are warnings about a project rather than a line, suppressed in that project's file with the reason:
CS8002 in `Avalon.Common` (the vendored DotRecast.Core it references has no strong name), ASPIRE010 in the AppHost
(it runs without the Aspire CLI bundle) and NU1510 on the framework packages `Avalon.Server.Auth` references.

## Exclusions

Generated and vendored code is not restyled and not reported:

- EF Core migrations and model snapshots (every `Migrations` folder), `*.g.cs`, `*.g.i.cs`, `*.generated.cs`,
  `*.Designer.cs`, and everything under `obj/`, `bin/` and `BenchmarkDotNet.Artifacts/`: `generated_code = true` in
  `.editorconfig`. Code generated at build time, such as the protobuf sources of `tests/Avalon.Wire.Reference`, is
  recognised by its `<auto-generated>` header.
- `vendor/`: the DotRecast submodule keeps its own `.editorconfig`. `vendor/Directory.Build.props` ends the
  `Directory.Build.props` search before the repository's, sets warning level 0, and adds `vendor/vendor.globalconfig`,
  which marks every vendored source generated, so neither the build nor `dotnet format` touches it.
