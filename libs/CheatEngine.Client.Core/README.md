# CheatEngine.Client.Core

The SDK-facing implementation of [CheatEngine.Client](https://www.nuget.org/packages/CheatEngine.Client). It is not a
standalone package: plugins never reference it directly.

## Context

`CheatEngine.Client.Core` maps the public contracts of `CheatEngine.Client.Abstractions` onto `CheatEngine.SDK` 2.x
while a Cheat Engine plugin activation is current: main-thread dispatch, runtime and target facts, target-selection
tracking, typed memory, AOB and value scans, copied inspection and Address List operations, protected Lua, target
allocations, instructions, Auto Assembler patches and the cleanup of every Client-owned resource. It is an in-process
layer for local, authorized targets; it is not an IPC client or a standalone Cheat Engine host.

`ICheatEngineClient` is the governing contract of that in-process model: it runs inside an enabled Cheat Engine plugin
on Cheat Engine's main thread. It is not a `ceserver` network client, not Cheat Engine's `luaclient` (CELUA) library,
and not a universal RPC client.

```text
Abstractions  ←  Core  ←  DependencyInjection  ←  Hosting
                    ↑
             CheatEngine.SDK

Abstractions  ←  Fluent
```

## Installation

Reference [`CheatEngine.Client`](https://www.nuget.org/packages/CheatEngine.Client), never Core on its own: the
[CheatEngine.Client README](https://github.com/CheatEngineNet/CheatEngine.Client/blob/main/src/CheatEngine.Client/README.md)
gives the plugin project, the requirements (`net10.0`, C# 14, a .NET SDK 10.0.401 or later, Cheat Engine 7.7.0.10621
x64, a direct `CheatEngine.SDK` reference in `[2.0.0, 3.0.0)`) and a minimal plugin. Hosting composes Core into the
activation-scoped `ICheatEngineClient`; a plugin obtains that facade from Hosting and never constructs a Core service.

Core has no public API. `CheatEngine.Client.Extensions.DependencyInjection` and `CheatEngine.Client.Hosting` use its
internal types through `InternalsVisibleTo`, and it is published only as a dependency of
`CheatEngine.Client.Extensions.DependencyInjection`. The seven Client packages ship in lockstep, with one version, and
each depends on the Client packages it builds on at exactly that version (`[X.Y.Z]` in its nuspec): a Core of another
version than the packages that call its internals is never a supported combination. The Client assemblies are not
strong-named, so an `InternalsVisibleTo` grant names an assembly, not a signing key; internal members are not a
contract and change in any release.

The package enables neither an SDK plugin entry point nor dynamic loading on its own. The plugin project references
both `CheatEngine.Client` and `CheatEngine.SDK` directly, so that the SDK generator, build assets, native Lua bridge and
host bootstrap run at the actual plugin boundary.

## What Core guarantees to a plugin

- Every Cheat Engine operation goes through the synchronous SDK main-thread dispatcher.
- An activation epoch is captured when the plugin is enabled, and stale work is refused instead of letting a resource
  survive a disable/re-enable boundary.
- Disable closes ordinary work admission, then releases Client-owned resources on Cheat Engine's main thread in reverse
  creation order while the SDK context is still valid. A lease-owned resource is created only while the activation is
  active, never from the cleanup scope.
- The plugin epoch and the target-selection epoch are separate, so a target change ends only the target-bound
  resources.
- SDK-owned scan and list data are copied into managed values before their owner is released: no SDK Lua state,
  Cheat Engine object or `Owned<T>` wrapper reaches plugin code.
- Expected host failures become a `CheatEngineFailure`, classified by exception type and the SDK's own status values,
  never by message text; programming errors stay ordinary .NET exceptions, and exceptions of your own callbacks, codecs
  and Lua operations are rethrown unchanged.
- Every runtime and target fact is a read-only CheatEngine.SDK 2.0.0 observation: a snapshot never loads a driver, runs
  remote code, changes the target, loads a table or allocates target memory.

The domains that no CheatEngine.SDK primitive backs (timers, hotkeys, the debugger, the speed hack, hashing, DBVM and
remote execution) have no Client contract and nothing in Core, as the
[Abstractions README](https://github.com/CheatEngineNet/CheatEngine.Client/blob/main/libs/CheatEngine.Client.Abstractions/README.md#not-offered-in-10)
states.

## Capability status

Every capability composes one operational adapter, so every implementation gate is satisfied. No Client qualification
receipt exists yet, so every qualification gate reports `Unknown` and no capability reports `Available`. The package
gate compares the CheatEngine.SDK identity this build consumed with the `CheatEngine.SDK.Engine` assembly actually
loaded: it is `Satisfied` for a release of the supported major at or above the consumed version, by SemVer precedence,
and its reason says whether the loaded assembly is exactly the reviewed package. The per-capability evidence and the
experimental APIs are in the
[Abstractions README](https://github.com/CheatEngineNet/CheatEngine.Client/blob/main/libs/CheatEngine.Client.Abstractions/README.md#capability-boundary).

## Cost of Cheat Engine calls

Every Client operation is synchronous and runs on Cheat Engine's main thread. A cancellation token is observed before
dispatch and between Client-managed steps; it never interrupts a Lua primitive that has already started.

- **Pointer-typed memory.** Every `ReadPrimitive<Address>` or `WritePrimitive<Address>` call, every `Address` primitive
  batch and every pointer chain observes the target facts once inside its dispatched call: about nine Lua global calls
  in one Lua admission (the selected process identifier twice, `isConnectedToCEServer`, `targetIs64Bit`, `targetIsX86`,
  `targetIsArm`, `targetIsAndroid`, `getABI`, `getPointerSize`). A batch pays this once for all its items, so hot
  pointer-read loops should use batches or explicit 32- or 64-bit integer reads.
- **AOB scans.** A request without a module or range runs one global `AOBScan` over the whole target. With a module or
  a range, a qualified local target runs an exhaustive MemScan limited to the module intersected with the range, which
  blocks Cheat Engine's main thread and cannot be interrupted once started; another target runs the global scan and the
  Client applies the module and range while copying, which does not reduce Cheat Engine's work. `MaximumResults`
  bounds only the copy. `IPatternScanner.ScanDetailed` measures the Cheat Engine scan and the Client copy separately.
- **Address List snapshots** copy the records up to the caller's limit; the child count of a record is still read
  through Cheat Engine's `Count` property.

## Diagnostics events

Core has no logging dependency. It reports bounded events to an internal sink carried by the
activation lifetime; `CheatEngine.Client.Extensions.DependencyInjection` writes them through
`Microsoft.Extensions.Logging` with source-generated events and one category per domain, so the
standard `Logging:LogLevel` filters select them:

| Event id | Level | Event | Category |
|---|---|---|---|
| 1000 | Debug | `RuntimeSnapshotCaptured`: activation epoch, target architecture, process and configured pointer bytes, mismatch flag | `CheatEngine.Client.Runtime` |
| 1001 | Debug | `CapabilityRefused`: capability id, operation, gate, gate state; once per capability and operation per activation | `CheatEngine.Client.Runtime` |
| 1100 | Debug | `TargetSelectionAdvanced`: activation and selection epochs, operation, reason (`PidChanged`, `ProcessReused`, `BackendChanged`, `ArchitectureChanged`, `WidthChanged`, `TargetDetached`) | `CheatEngine.Client.Processes` |
| 1200 | Information | `PointerWidthMismatchRefused`: operation, process and configured pointer bytes | `CheatEngine.Client.Memory` |
| 1201 | Debug | `MemoryBatchCompleted`: operation, requested and completed counts, effect state | `CheatEngine.Client.Memory` |
| 1300 | Debug | `TableGenerationAdvanced`: activation epoch, table generation | `CheatEngine.Client.Tables` |
| 1301 | Debug | `StaleRecordIdentifierRefused`: operation, table generation | `CheatEngine.Client.Tables` |
| 1302 | Debug | `RecordActivationNotApplied`: operation, requested state, status (`RefusedByHost`, `Pending`, `Indeterminate`) | `CheatEngine.Client.Tables` |
| 1400 | Debug | `SymbolRegistrationRejected`: operation, reason (`AlreadyResolves`, `LookupFailed`) | `CheatEngine.Client.Inspection` |
| 1500 | Debug | `PatternScanCompleted`: scope, host result and materialized counts, truncation, scan and copy milliseconds | `CheatEngine.Client.Scanning` |
| 1600 | Debug | `LuaOperationCompleted`: operation, failure kind or `None`, milliseconds, script length (unsafe Lua only) | `CheatEngine.Client.Lua` |
| 1700 | Warning | `CoreResourceCleanupFailed`: resource and exception type names | `CheatEngine.Client.Lifetime` |
| 1701 | Debug | `LeaseReleased`: release operation, `LeaseReleaseKind`, host effect | `CheatEngine.Client.Lifetime` |
| 1800 | Warning | `AutoAssemblerPatchAppliedAfterTargetChange`: operation, selection epoch the patch stays bound to | `CheatEngine.Client.Assembly` |

Events are emitted after the dispatched Cheat Engine work returned, never inside a dispatched
callback. They never carry an address, a value, a symbol or module name, a path, a process
identifier or name, a Lua script or its text, an exception message or a failure object. A logger
or provider that throws is contained and never changes an operation result or a cleanup. Hosting adds its own
activation and cleanup events, described in the
[Hosting README](https://github.com/CheatEngineNet/CheatEngine.Client/blob/main/libs/CheatEngine.Client.Hosting/README.md#cleanup-diagnostics-and-redaction).
