# CheatEngine.Client.Abstractions

## Context

`CheatEngine.Client.Abstractions` is the stable, handle-free vocabulary of the in-process
`CheatEngine.Client` plugin API. It targets C# 14 and .NET 10 on top of
[CheatEngine.SDK](https://www.nuget.org/packages/CheatEngine.SDK), and is valid only for the
current Cheat Engine plugin activation.

It defines the public contracts used by the product facade, implementations, fluent helpers,
hosting, extensions, and application code. It is intentionally synchronous: an attached Cheat
Engine Lua runtime and its main-thread work must not be retained across an `await` boundary.

## Why This Project Exists

The SDK correctly exposes the Cheat Engine runtime, Lua bridge, ownership wrappers, and ABI-level
concepts. Application code should not need to carry those handles through its own architecture.
This project establishes a smaller product boundary with explicit failure, ownership, lifetime,
and materialization rules.

It is the bottom of the Client project graph: it has no Client project reference. Its only package
dependency is the `CheatEngine.SDK` compile/runtime surface required for stable value and query
types such as `Address`, target identifiers, inspection metadata, scan requests, and address-list
identifiers. SDK build assets, generators, native assets, and analyzers deliberately do not flow
through this package.

```text
CheatEngine.Client.Abstractions
          ↑              ↑
       Fluent          Core
                         ↑
              DependencyInjection / Hosting
```

## How It Improves CheatEngine.Client

- Makes domain behavior testable against interfaces instead of Cheat Engine statics.
- Keeps expected failures explicit through `Try...(..., out CheatEngineFailure)` and throws
  `CheatEngineClientException`-derived exceptions only from the convenience methods.
- Keeps CE-owned objects out of the public surface: no `LuaState`, `LuaRef`, `CEObject`,
  `Owned<T>`, or raw native handle escapes this package.
- Requires bounded copies for scans, table snapshots, strings, byte reads, pointer chains, and
  inspection collections, avoiding unbounded materialization and leaked SDK ownership.
- Makes lifecycle constraints visible: `ICheatEngineClient.Epoch`, `Stopping`, leases, and scan
  sessions are activation-scoped; stale resources report `CheatEngineActivationExpiredException`.

## Public Surface

Package and assembly names are not consumer namespaces. Public code belongs to functional
namespaces only:

| Namespace | Responsibility |
|---|---|
| `CheatEngine.Client` | `ICheatEngineClient`, the activation-scoped facade |
| `.Dispatching` / `.Runtime` | main-thread dispatch and runtime/capability observations |
| `.Processes` / `.Inspection` | target selection, copied process/module/region/symbol data |
| `.Memory` | bounded primitive, byte, string, codec, and pointer-chain operations |
| `.Scanning` | AOB contracts and the value-scan session contract |
| `.Tables` | copied Address List records and explicitly trusted table I/O requests |
| `.Lua` / `.Modules` | typed protected Lua operations, explicit modules, and leases |
| `.Results` | classified expected failures and lifecycle exceptions |

No public consumer should use `CheatEngine.Client.Abstractions` as a namespace.

### Capability Boundary

The contracts describe runtime, process, memory, inspection, AOB scanning, tables, protected Lua,
and explicitly disposable value-scan sessions. A contract is not an availability promise: callers
must inspect `ICheatEngineRuntime` capability observations or handle `CapabilityUnavailable`.

In particular, the value-scan contract and state model are published, but the Core implementation
does **not** currently create a live `MemScan`/`FoundList` session. CheatEngine.SDK 1.0.0 has no
public ownership factory for the required objects; Client enablement remains blocked by the
Cheat Engine 7.7 x64 ownership and reactivation live gate. Do not treat `IValueScanner` as an
available v1 runtime feature.

`IUnsafeLuaClient` is intentionally separate from `ILuaClient` and is not registered by default.
It is for explicitly trusted source only and still never exposes a raw Lua state.

## Contribution and Validation

Changes here are public API changes. Keep request/value types immutable, preserve functional
namespaces, add XML documentation, update `PublicAPI.Unshipped.txt`, and add focused contract
tests in `tests/CheatEngine.Client.Abstractions.Tests` before moving an entry to
`PublicAPI.Shipped.txt`.

From the repository root, validate the complete package graph:

```powershell
dotnet restore CheatEngine.Client.slnx --locked-mode
dotnet build CheatEngine.Client.slnx --configuration Release --no-restore
dotnet test --solution CheatEngine.Client.slnx --configuration Release --no-build --no-restore
```
