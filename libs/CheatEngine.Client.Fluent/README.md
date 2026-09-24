# CheatEngine.Client.Fluent

## Context

`CheatEngine.Client.Fluent` supplies the immutable, handle-free syntax for common Client
operations. It enriches the contracts in `CheatEngine.Client.Abstractions`; it does not execute
Cheat Engine calls by itself.

The package currently provides AOB request builders and typed target-memory address builders. A
terminal builder delegates work to a caller-supplied `IPatternScanner` or `IMemoryClient`, usually
the services available from an activation-scoped `ICheatEngineClient`.

```csharp
using CheatEngine.Client.Memory;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Values;

Address address = client.Aob("48 8B ?? ?? ?? 89")
    .InModule("game.exe")
    .Executable()
    .RequireSingle()
    .Execute();

client.Memory.At(address + 0x14).Write(999);
```

`InModule(...)` and `InRange(...)` scope the scan. On a qualified local target Cheat Engine runs an exhaustive MemScan
limited to the module intersected with the range; it blocks Cheat Engine's main thread and cannot be interrupted once
started. On a CEServer or file-as-process target, Cheat Engine runs one global `AOBScan` over the whole target and Core
keeps only the addresses inside the module or range, which does not reduce Cheat Engine's scan time or memory.
`Take(n)`, `FirstOrNone()` (1) and `RequireSingle()` (2) bound only how many addresses Core copies; they never stop
Cheat Engine early. `FirstOrNone()` returns the first element in Cheat Engine's result-list order, which Cheat Engine
does not specify (not the lowest address, not the first logical region). `RequireSingle()` copies up to two matches
from an exhaustive scan, so a truncated copy is reported as ambiguous. Only the bounded route reports a factual zero
(`null`, `NotFound`); on a global route a scan that finds nothing is reported as `IndeterminateHostResult` (on Cheat
Engine 7.7 `AOBScan` returns `nil` for zero matches and for some host failures alike). A cancellation token cannot
interrupt a scan that Cheat Engine has started.
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

- Represents operation configuration as immutable `readonly record struct` values rather than CE
  handles or mutable builders.
- Validates and normalizes an AOB pattern and its options before a terminal operation is selected:
  `Executable()`, `Writable()` and `WithProtection(...)` set the protection filter, `AlignedTo(...)` and
  `WithLastDigits(...)` the alignment rule.
- Forces explicit result cardinality: `RequireSingle()`, `FirstOrNone()`, or `Take(maximumResults)`.
- Preserves materialization-bounded copies: the limit bounds only the number of copied addresses, never Cheat
  Engine's scan. Callers inspect `AobScanResult.IsTruncated` when a copy is intentionally incomplete.
- Provides `Memory.At(...)` and `memory.At(...)` builders for primitive and codec-based reads and
  writes without retaining a live target handle, including exact byte copies, explicit UTF-8/UTF-16
  bounds, finite pointer chains, and bounded homogeneous primitive batches. The primitive terminals
  and `Batch<T>` take `where T : unmanaged`, like `IMemoryClient`, which supports the 8- to 64-bit
  integers, `float`, `double` and `Address`; other types go through `ReadWith`/`WriteWith` and a codec.
- Uses the normal `Try...` plus `CheatEngineFailure` pattern and leaves the actual lifecycle,
  dispatch, and SDK translation to the supplied contract implementation.

## Public Namespaces and Boundaries

The package publishes functional namespaces only:

| Namespace                     | Entry points                                                          |
|-------------------------------|-----------------------------------------------------------------------|
| `CheatEngine.Client.Scanning` | `Aob(...)`, AOB module and range scopes, and copy-bounded terminals   |
| `CheatEngine.Client.Memory`   | `Memory.At(...)`, `IMemoryClient.At(...)`, and `MemoryAddressBuilder` |

`CheatEngine.Client.Fluent` is a package/assembly name, never a consumer namespace. The builders
may expose stable SDK value types already present in the Abstractions vocabulary, notably `Address`
and `ModuleName`; AOB options are the Client-owned `ScanProtectionFilter` and `ScanAlignment`. They
never expose Lua states, CE objects, SDK option types, or SDK ownership wrappers.

Fluent does not make a capability available. For example, it has no value-scan builder and cannot
turn the currently gated `IValueScanner` contract into a live scan. A builder remains valid as a
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
