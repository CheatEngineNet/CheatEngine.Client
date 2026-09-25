# CheatEngine.Client.Fluent.Tests

## Context

This project tests the public fluent builders for AOB scanning and typed memory access. The builders are pure managed
values; they do not retain a Cheat Engine object, Lua state, or SDK ownership handle.

## Why this project exists

The fluent layer is the consumer-facing expression of limits and intent. It must preserve the caller's pattern,
module/range/protection/alignment filters, codec choice, and bounded terminal behavior until execution is delegated to
the Client facade.

## How it helps improve CheatEngine.Client

The tests prove builder immutability, correct forwarding, invalid-argument rejection, and the bounded semantics of
`FirstOrNone`, `RequireSingle`, and `Take`. This keeps ergonomic calls such as `client.Patterns.Aob(...).Take(...)`
predictable without making a live scan part of a unit test. `FluentSurfaceTests` pins the surface itself: each domain
has one entry point bound to its service (`IPatternScanner.Aob`, `IMemoryClient.At` and `IMemoryClient.Batch<T>`), a
builder declares no constructor (only a struct's implicit parameterless one), factory or rebinding method, the seven
builders are plain `readonly struct` values that declare no equality members or `ToString`, every operation of a
`default` builder throws the `InvalidOperationException` its XML documentation declares, and every terminal documents
the Client exceptions it can raise.

The AOB terminals read `IPatternScanner.ScanDetailed`, so the test scanner publishes a detailed outcome whose metrics
match its copied result. The tests prove that `null`, `NotFound` and an empty `Take` result come only from a factual
zero (every row Cheat Engine returned was read), that an unread row or a `nil` result stays `IndeterminateHostResult`,
and that `RequireSingle` reports `AmbiguousMatch` only for a second copied match. The test scanner is a model of Core;
`Composition/FluentAobTerminalCompositionTests` in `CheatEngine.Client.Core.Tests` runs the same terminals against
Core's real `PatternScanner` on the bounded, managed-filter and unscoped routes. The memory tests prove that each
terminal passes the caller's token unchanged, so a cancelled token surfaces as an `OperationCanceledException`.

## Run

From the repository root:

```powershell
dotnet test --project .\tests\CheatEngine.Client.Fluent.Tests\CheatEngine.Client.Fluent.Tests.csproj --configuration Release
```
