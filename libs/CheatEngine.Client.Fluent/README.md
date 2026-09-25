# CheatEngine.Client.Fluent

## Context

`CheatEngine.Client.Fluent` supplies the immutable, handle-free syntax for common Client
operations. It enriches the contracts in `CheatEngine.Client.Abstractions`; it does not execute
Cheat Engine calls by itself.

The package currently provides AOB request builders and typed target-memory address builders. Each
domain has one entry point, an extension method on the service that runs its terminal operations:
`scanner.Aob(pattern)` on an `IPatternScanner`, and `memory.At(address)` or `memory.Batch<T>()` on an
`IMemoryClient`, usually `client.Patterns` and `client.Memory` of an activation-scoped
`ICheatEngineClient`. A builder is bound to that service when it is created and is never rebound.

```csharp
using CheatEngine.Client.Memory;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Values;

Address address = client.Patterns.Aob("48 8B ?? ?? ?? 89")
    .InModule("game.exe")
    .Executable()
    .RequireSingle()
    .Execute();

client.Memory.At(address + 0x14).Write(999);
```

`InModule(...)` and `InRange(...)` scope the scan with one rule on every route: a match must lie entirely inside the
module, and its start must lie in the range. On a qualified local target Cheat Engine runs an exhaustive MemScan
limited to the module intersected with the range; it blocks Cheat Engine's main thread and cannot be interrupted once
started. On a CEServer or file-as-process target, Cheat Engine runs one global `AOBScan` over the whole target and Core
applies the same rule while copying, which does not reduce Cheat Engine's scan time or memory.
`Take(n)`, `FirstOrNone()` (1) and `RequireSingle()` (2) bound only how many addresses Core copies; they never stop
Cheat Engine early. Every route copies at most 65,535 addresses, whatever `n`, as `AobScanRequest.MaximumResults`
documents. `IsTruncated` reports a copy that is not proven complete: one cut by either limit, or, on the bounded route,
a destination that filled up with rows outside the request while rows stayed unread. `FirstOrNone()` returns the first
element in Cheat Engine's result-list order, which Cheat Engine does not specify (not the lowest address, not the first
logical region), and never uses a "first found" scan. `RequireSingle()` copies up to two matches from an exhaustive
scan: two copied matches are `AmbiguousMatch`, and one copied match is unique only when every row Cheat Engine returned
was read and the copy is not truncated. Otherwise whether a second match exists is unknown, which is
`IndeterminateHostResult`, never `AmbiguousMatch`.

The terminals read `IPatternScanner.ScanDetailed`, whose metrics say whether every row Cheat Engine returned was
read. `null`, `NotFound` and an empty `Take` result are factual zeros only: the scan succeeded, every row was read,
and none lay inside the request.

| Route (`PatternScanScope`)        | Factual zero                            | Never a zero             |
|-----------------------------------|-----------------------------------------|--------------------------|
| `HostBoundedRange`                | No in-bounds row (error text readable)  | A failed scan            |
| `GlobalHostScanWithManagedFilter` | Every listed row outside the request    | `nil`, an unread row     |
| `GlobalHostScan`                  | An empty list that Cheat Engine returns | `nil` (Cheat Engine 7.7) |

A global scan for which Cheat Engine returns no result list is reported as `IndeterminateHostResult` (on Cheat
Engine 7.7 `AOBScan` returns `nil` for zero matches and for some host failures alike), and so is an empty copy that
did not read every row. A cancellation token cannot interrupt a scan that Cheat Engine has started.
`IPatternScanner.ScanDetailed` reports the route, the host outcome, the host result count, the examined, filtered and
copied counts, and the Cheat Engine scan time separately from the copy time.

## Why This Project Exists

The public API needs expressive construction of bounded requests without coupling application code
to Core, service location, or SDK ownership. Fluent keeps that syntax as a small pure layer over
interfaces, so builders can be inspected and tested without Cheat Engine.

It references `CheatEngine.Client.Abstractions` only. It has no project reference to Core and no
direct `CheatEngine.SDK` package reference. Core remains the only Client layer that maps a terminal
operation to Cheat Engine; Dependency Injection and Hosting own the concrete implementation.

```text
Abstractions  ←  Fluent
      ↑
     Core  ←  DependencyInjection  ←  Hosting
```

## How It Improves CheatEngine.Client

- Represents operation configuration as immutable, plain `readonly struct` builders rather than CE
  handles or mutable builders. A builder declares no `Equals`, `GetHashCode`, `ToString` or equality
  operators (only `System.ValueType`'s): compare the requests or addresses it carries. It has no
  constructor beyond the implicit parameterless one, which yields the `default` value: that value has
  no service, and every operation that runs or selects a terminal throws a documented
  `InvalidOperationException` on it.
- Validates and normalizes an AOB pattern and its options before a terminal operation is selected:
  `Executable()`, `Writable()` and `WithProtection(...)` set the protection filter, `AlignedTo(...)`,
  `LastDigits(...)` and `WithAlignment(...)` the alignment rule. A shortcut is named like the
  `ScanAlignment` factory it calls, and `With<Option>(...)` sets a whole option value.
- Forces explicit result cardinality: `RequireSingle()`, `FirstOrNone()`, or `Take(maximumResults)`.
- Preserves materialization-bounded copies: the limit bounds only the number of copied addresses, never Cheat
  Engine's scan. Callers inspect `AobScanResult.IsTruncated`, which reports a copy that is not proven complete.
- Provides `memory.At(...)` and `memory.Batch<T>()` builders for primitive and codec-based reads and
  writes without retaining a live target handle, including exact byte copies, strings with an explicit
  `MemoryStringEncoding` and length bound (`ReadString`/`TryReadString`, `WriteString`/`TryWriteString`),
  finite pointer chains, and bounded homogeneous primitive batches. The primitive terminals
  and `Batch<T>` take `where T : unmanaged`, like `IMemoryClient`, which supports the 8- to 64-bit
  integers, `float`, `double` and `Address`; other types go through `ReadWith`/`WriteWith` and a codec.
- Uses the normal `Try...` plus `CheatEngineFailure` pattern and leaves the actual lifecycle,
  dispatch, and SDK translation to the supplied contract implementation. A throwing terminal raises
  the exception that `CheatEngineFailure.Throw(CancellationToken)` maps from the failure kind; both
  forms throw `CheatEngineActivationExpiredException` or `CheatEngineInvalidStateException` when
  the activation has ended or is stopping, and every terminal documents these exceptions.

## Public Namespaces and Boundaries

The package publishes functional namespaces only:

| Namespace                     | Entry points                                                                    |
|-------------------------------|---------------------------------------------------------------------------------|
| `CheatEngine.Client.Scanning` | `IPatternScanner.Aob(...)`, AOB module and range scopes, copy-bounded terminals |
| `CheatEngine.Client.Memory`   | `IMemoryClient.At(...)`, `IMemoryClient.Batch<T>()`, and `MemoryAddressBuilder` |

`CheatEngine.Client.Fluent` is a package/assembly name, never a consumer namespace. The builders
may expose stable SDK value types already present in the Abstractions vocabulary, notably `Address`
and `ModuleName`; AOB options are the Client-owned `ScanProtectionFilter` and `ScanAlignment`. They
never expose Lua states, CE objects, SDK option types, or SDK ownership wrappers.

Fluent does not make a capability available. For example, it has no builder for the experimental
value scans (`CECLIENT5001`): use `IValueScanner` directly. A builder remains valid as a
managed value, but executing it through a stale scoped service still follows the implementation's
activation and target-epoch rules.

## Contribution and Validation

Add a fluent surface only when it preserves an existing explicit contract and has a bounded terminal
operation. Do not store CE resources in a builder, add Core dependencies, or introduce
assembly-derived namespaces. Update `PublicAPI.Unshipped.txt` and add focused behavior tests in
`tests/CheatEngine.Client.Fluent.Tests` for every public member or terminal-condition change.

Validate the complete graph from the repository root:

```powershell
dotnet restore CheatEngine.Client.slnx --locked-mode
dotnet build CheatEngine.Client.slnx --configuration Release --no-restore
dotnet test --solution CheatEngine.Client.slnx --configuration Release --no-build --no-restore
```
