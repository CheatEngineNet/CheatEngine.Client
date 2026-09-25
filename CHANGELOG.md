# Changelog

All notable changes to the CheatEngine.Client packages are documented in this file. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/). The seven packages (`CheatEngine.Client`, `.Abstractions`, `.Core`,
`.Extensions.DependencyInjection`, `.Fluent`, `.Hosting` and `.Templates`) always share one version.

Each release is a `## [X.Y.Z] - YYYY-MM-DD` section dated in ISO 8601, and its body becomes the notes of the GitHub
release. Earlier builds packed `0.1.0` from source, but no `CheatEngine.Client*` package exists on nuget.org before
1.0.0, so this file starts at the first release.

Each release separates four kinds of change, because a consumer reacts to each differently:

- **Added**: an extension, such as a new API, option or package asset. Existing code keeps its meaning.
- **Changed**: a semantic correction. An existing API returns, throws, cancels or cleans up differently, even when its
  signature is unchanged.
- **Security**: a hardening. An operation that used to proceed is now refused, refused earlier or gated behind an
  opt-in, or data that used to reach a log no longer does.
- **Deployment**: a change to what is built, packed, pinned, signed or published, and to how a plugin is deployed.

The 1.0.0 section also has a **Removed** list: what the repository offered before its first release and 1.0.0 does not
ship.

## [Unreleased]

### Added

### Changed

### Security

### Deployment

## [1.0.0] - 2026-09-25

The first release of CheatEngine.Client: high-level, activation-scoped C# APIs for Cheat Engine 7.7 x64 plugins, built
on CheatEngine.SDK 2.0.0. It fixes the public API for the 1.x line, under the versioning rules of the
[README](https://github.com/CheatEngineNet/CheatEngine.Client/blob/main/README.md#versioning-and-compatibility) and the
public API charter of the `CheatEngine.Client.Abstractions` README; the experimental APIs `CECLIENT5001` to
`CECLIENT5004` stay outside that promise. **Changed** and **Removed** compare this release with the `0.1.0` builds of
the repository, which were never published.

### Added

- **One client per activation.** `CheatEngineClientPlugin` builds a new DI provider, scope and `ICheatEngineClient` on
  every enable, starts the `ICheatEngineClientModule`s in registration order, stops them in reverse order and releases
  every Client-owned Cheat Engine resource while the SDK context is still valid. `ICheatEngineClient` exposes
  `Dispatcher`, `Runtime`, `Processes`, `Memory`, `Patterns`, `Inspection`, `Tables` and `Lua`, and the experimental
  `ValueScans`, `Allocations` and `Assembly`; `Epoch` and `Stopping` identify the activation.
- **Capability matrix.** `ICheatEngineRuntime.TryGetClientCapability` reports each of the eleven `ClientCapabilityId`s
  with its `Evidence`: separate implementation, package, host, live qualification, policy and lifetime gates, and a
  typed `EffectiveReasonCode`. Every capability has an operational adapter. The package gate accepts a loaded
  CheatEngine.SDK 2.x at or above 2.0.0, and the qualification gate stays `Unknown` until committed Client receipts
  exist for the capability's live scenarios, so no capability reports `Available` in this release. The README
  capability tables state the same matrix, and the features Cheat Engine offers without a CheatEngine.SDK primitive are
  listed as not offered.
- **Experimental APIs.** Four operational adapters are marked `[Experimental]`. Suppressing the diagnostic is the
  opt-in, each diagnostic links to its section of the Abstractions README (scope, behavior, known limits and exit
  criteria), and each id is lifted only when its capability's live scenarios succeed:
  - `CECLIENT5001`, value scans: `ICheatEngineClient.ValueScans` creates `IValueScanSession`s over CheatEngine.SDK's
    owned `MemScan` and `FoundList`, with first and next scans and pages of at most 1024 copied results;
    `ValueScanValue.FromSingle` and `FromDouble` write the value in fixed point with the decimals the caller passes;
  - `CECLIENT5002`, target allocations: `IAllocationClient.Allocate` returns an `ITargetMemoryLease` over
    CheatEngine.SDK's `AllocatedRegion` (`AllocationProtection.ReadWrite` or `ExecuteReadWrite`), freed only in the
    process incarnation that made it;
  - `CECLIENT5003`, instructions: `IAssemblyClient` assembles, disassembles and measures one instruction per call,
    checked against the target's instruction profile, and never writes target memory;
  - `CECLIENT5004`, Auto Assembler patches: `IAutoAssemblerClient` checks a script, or applies it and returns an
    `IAutoAssemblerPatchLease` that owns the disable information Cheat Engine returned; it is registered only by
    `EnableAutoAssemblerPatches()`.
- **Leases and release outcomes.** Every resource the Client creates for the caller is an `ICheatEngineLease`:
  symbol registrations, Lua modules, value-scan sessions, allocations and Auto Assembler patches. `Release()` returns a
  `LeaseReleaseOutcome` (a `LeaseReleaseKind`, the host effect, and exactly one of `IsComplete`, `IsRetryable` and
  `RequiresManualRecovery`), `Dispose()` never throws, `LastReleaseOutcome` keeps the attempt that ended the lease, and
  `RequiresManualRecovery` says that what the lease owned may remain. Only `Unknown` and `CleanupUnavailable` are
  retryable, as in CheatEngine.SDK; a lease still incomplete at disable is reported in the aggregated deactivation
  failure. A target-bound lease exposes the `SelectionEpoch` of the process CheatEngine.SDK bound it to, and
  `ILuaModuleLease.LastModuleReleaseOutcome` keeps what the module reported.
- **Failure vocabulary.** `CheatEngineFailure.HostEffect` says how far the Cheat Engine primitive got: `NotStarted`,
  `Started`, `Completed`, `NotApplied`, `CleanupUnconfirmed` or `Unknown`. New failure kinds:
  `IndeterminateHostResult` (documented causes that cannot be told apart, never absence), `TargetChanged`,
  `TargetIdentityUnavailable` and `RuntimeChanged`. `CheatEngineFailure.Throw(token)` throws a failure's exception and
  `ToException(token)` creates it.
- **Pattern scans.** `IPatternScanner.ScanDetailed` returns a `PatternScanOutcome`: the classification of `TryScan`,
  `PatternScanMetrics` (the route, the host result count, the examined, filtered-out, copied and unread counts, and
  Cheat Engine's scan time apart from the copy time), the `PatternScanHostOutcomeKind`, the `PatternScanRouteReason`
  and `TargetIdentityVerified`. A module or range scan runs Cheat Engine's bounded MemScan on a qualified local target
  and falls back to one global `AOBScan` with managed filters elsewhere, whose addresses are never reported as
  identity-verified. Scan options are Client values (`ScanProtectionFilter`, `ScanAlignment`) that Core translates the
  same way on every route; the default filter is Cheat Engine's empty "find everything" protection text.
- **Memory.** `IMemoryClient.ReadBytesDetailed` returns the confirmed prefix of a partial byte read
  (`MemoryBytesReadOutcome`), and `ReadPrimitiveBatchDetailed` and `WritePrimitiveBatchDetailed` report
  `RequestedCount`, `CompletedCount` and a `MemoryBatchWriteEffectState`. Pointer-typed operations use
  CheatEngine.SDK's width-qualified reads and writes at the observed process width, and codec contexts expose `Bitness`
  and the configured pointer size.
- **Runtime and target facts.** `CheatEngineRuntimeSnapshot` groups the four-part Cheat Engine version, the loaded
  CheatEngine.SDK version and whether it is the reviewed package (`Version`), and the host operating system and
  architecture, `CheatEngineBitness`, the `TargetBackend`, the target architecture, `TargetBitness` and the configured
  pointer size (`Platform`); a fact CheatEngine.SDK could not establish stays unknown. `ProcessSnapshot` adds `Backend`,
  `Bitness`, `StartTimeUtc` and `SelectionEpoch`, and `IProcessClient.TryGetLocalProcesses` reads the local process
  catalog offline, without an activation.
- **Address List and symbols.** `ITableClient` counts records (`GetRecordCount`), reads one by index (`GetRecordAt`),
  reads the selected record and selects one (`GetSelectedRecord`, `SelectRecord`), and loads and saves trusted tables
  through CheatEngine.SDK's `CheatTableFiles`; a trusted load makes every earlier `MemoryRecordId` stale.
  `IInspectionClient.TryRegisterSymbol` registers through CheatEngine.SDK's owned registration after a collision check,
  and `TryResolveAddress` takes an `AddressResolutionMode`.
- **Lua.** Generated Lua modules register through CheatEngine.SDK 2.0.0 registration leases, and the generator reports
  the module shape errors `CECLUA1201` to `CECLUA1204`.
- **Fluent entry points.** Each domain has one entry point, an extension method bound to its service when the builder
  is created: `memory.At(address)` and `memory.Batch<T>()` on an `IMemoryClient`, and `scanner.Aob(pattern)` on an
  `IPatternScanner`. The AOB builder takes typed options (`Writable()`, `WithProtection`, `AlignedTo`, `LastDigits`,
  `WithAlignment`), and every terminal documents the exceptions it throws.
- **Hosting.** `CheatEnginePluginBuilder.Logging` is the `ILoggingBuilder` of the activation provider, and
  `PluginDirectory` is the folder of the plugin assembly, the base for the plugin's own files.
  `builder.Logging.AddCheatEngineHostLog()` adds an opt-in logging provider that writes to CheatEngine.SDK's host log.
  Event 20 (`ActivationIdentified`) identifies each activation with the Client version and the consumed and loaded
  CheatEngine.SDK, and event 8 (`ExternalLuaStateResetDetected`) reports a Lua state that Cheat Engine replaced outside
  the plugin's control, read from CheatEngine.SDK's flag after the Client resources are released, without a snapshot.
- **Build diagnostics.** The Hosting README catalogs the consumer diagnostics `CECLIENT001` to `CECLIENT017`, and each
  diagnostic carries a help link to its entry.
- **Template.** `dotnet new ceplugin` derives the plugin's display name and its Lua status global (ASCII
  lower_snake_case with a `_status` suffix) from the project name, creates a folder named after the project, honors
  `--no-restore` and ships a `.gitignore`. The generated plugin adds the host log provider and reads its configuration
  from `PluginDirectory`.
- **Documentation.** Every public member documents its parameters, return value and exceptions, which a test checks.
  The packed READMEs are written for plugin authors, and their C# snippets compile against the packed packages.

### Changed

- **Cancellation.** A throwing form whose token is observed throws `CheatEngineOperationCanceledException`, an
  `OperationCanceledException` whose `Failure.HostEffect` says whether Cheat Engine work had started. A token is
  observed before dispatch and between Client-managed steps, and never interrupts a Cheat Engine call that has started.
  A batch write cancelled before dispatch reports `MemoryBatchWriteEffectState.NotStarted`.
- **Exceptions.** The exception type depends only on the failure kind: `Cancelled` gives
  `CheatEngineOperationCanceledException`, `ActivationExpired` `CheatEngineActivationExpiredException`, `InvalidState`
  `CheatEngineInvalidStateException` (formerly `CheatEngineClientLifecycleException`), and every other kind
  `CheatEngineOperationException`. Every exception keeps the complete failure, and no Client exception has a public
  constructor: `CheatEngineFailure.Throw(token)` and `ToException(token)` create them.
- **Arguments first.** A null argument, a `default` request, an undefined enum value or an out-of-range number throws
  an `ArgumentException` from the `Try` form as from the throwing form, before the activation check and before any
  Cheat Engine call; it is never returned as a failure. An ended activation then throws
  `CheatEngineActivationExpiredException` and a stopping one `CheatEngineInvalidStateException`, under the operation's
  own name, before any refusal of the request is returned.
- **Try forms.** No `Try` form lets a CheatEngine.SDK exception escape: an SDK fault raised by Client-internal work
  becomes a `CheatEngineFailure` chosen by exception type and by the SDK's failure category. Exceptions from your own
  callbacks, codecs, Lua operations and Lua result mappers are rethrown unchanged. A Lua admission that the Client asks
  for and CheatEngine.SDK refuses is `ActivationExpired`, `RuntimeChanged`, `InvalidState` or `IndeterminateHostResult`
  with `NotStarted`.
- **Classification.** `CheatEngineFailure.Operation` is `<Service>.<Member>` (for example `Memory.ReadPrimitive`, or
  `Client.Activate` for the activation itself), and the `default` failure is safe to read. An outcome or status of
  CheatEngine.SDK that the Client does not recognize fails closed as `IndeterminateHostResult`, never as a success. A
  host rollback or release that was not confirmed is reported with `CleanupUnconfirmed`, under
  `IndeterminateHostResult` or the kind of the failure that caused it. An unavailable Address List or inspection global
  is `CapabilityUnavailable` with `NotStarted` in every lookup, and a record activation refused by its callback, script
  or record type is `OperationRejected` with `Started`.
- **Leases.** A lease no longer throws from `Dispose()`: `Release()` returns the outcome, and a repeated release returns
  the outcome that ended the lease without calling Cheat Engine. `RequiresManualRecovery` becomes `true` once a release
  attempt leaves the resource behind; `IAutoAssemblerPatchLease.CanDisable` is the earlier signal. Core and Hosting
  attempt every cleanup in reverse order and report every failure: one failure is rethrown as the same instance,
  several are aggregated, and Hosting logs each failed stage. A failed Address List record creation destroys the
  partial record once and reports `CleanupUnconfirmed` when that rollback is not confirmed.
- **Target selection.** `IProcessClient.Attach` goes through CheatEngine.SDK's `SelectAndObserve` and reads the
  selected process again before it reports success; a file opened as a process is `TargetIdentityUnavailable`. A
  target-bound lease ends when the Client observes that Cheat Engine selected another process, and that release is
  refused (`RefusedTargetChanged`) without freeing anything: release target-bound leases before selecting another
  process.
- **AOB scans.** One scope rule applies on every route: a match lies wholly inside the module and starts inside the
  range. Every route copies at most 65,535 addresses, and `MaximumResults`, `Take`, `FirstOrNone` and `RequireSingle`
  bound only that copy, never Cheat Engine's scan. A global scan for which Cheat Engine returns no result list fails
  with `IndeterminateHostResult` instead of `OperationRejected`: the global route calls CheatEngine.SDK's
  `AobScanner.TryScanOutcome`, and on Cheat Engine 7.7 `AOBScan` returns `nil` for zero matches and for some host
  failures alike, so it is never reported as `null` or `NotFound`. A result list or MemScan session is released exactly
  once on every path; when that release is not confirmed, the copy is discarded and the scan fails with
  `IndeterminateHostResult`, or the kind of the failure that caused it, and `CleanupUnconfirmed`. `IsTruncated` means
  that the copy is not proven complete, and `RequireSingle` reports a single match that is not proven unique as
  `IndeterminateHostResult`, not `AmbiguousMatch`.
- **Memory.** `IMemoryClient` never resolves a codec: pass the `IMemoryCodec<T>` in a `MemoryReadRequest<T>` or
  `MemoryWriteRequest<T>`. Primitives take `where T : unmanaged` and support the 8- to 64-bit integers, `float`,
  `double` and `Address`; any other `T` is `OperationRejected` with `NotStarted`, without a Cheat Engine call. Pointers
  follow the observed process width, never the configured size: an unknown or mismatched width is refused before any
  access, and nothing is truncated on a 32-bit target. String requests carry an explicit `MemoryStringEncoding`.
- **Implementable interfaces.** `IMemoryCodec<T>.TryRead` and `TryWrite` report an `out CheatEngineFailure failure`,
  and their contexts report the same on `TryReadBytes` and `TryWriteBytes`. `ILuaModule` carries its `Descriptor`, and
  `Unregister()` returns a `LuaModuleReleaseOutcome`. These interfaces are frozen for 1.x.
- **Operations renamed or reshaped.** `ICheatEngineClient.Scans` is `ValueScans`; `IProcessClient.GetCurrent` is
  `GetCurrentProcess`; `ITableClient.GetRecord(int)` is `GetRecordAt`, `GetSelected` is `GetSelectedRecord`,
  `TrySelect` is `TrySelectRecord` and `Update(update)` is `Update(id, update)`; `IAssemblyClient.GetInstructionSize`
  is `GetInstructionLength` and `GetPreviousInstruction` is `GetPreviousInstructionAddress`, and `ApplyPatch` moved to
  `IAutoAssemblerClient`; `TargetAllocationRequest` is `AllocationRequest` and `TargetAllocationAccess` is
  `AllocationProtection`. Value-scan sessions take `ValueScanFirstRequest` and `ValueScanNextRequest` (`TryFirstScan`,
  `TryNextScan`), and `TryResolveAddress` an `AddressResolutionMode`, instead of CheatEngine.SDK request and option
  types. `ICheatEngineDispatcher` has explicit overloads instead of an optional token, and `ILuaClient` one
  `TryExecute<TOperation, TResult>(in TOperation ...)` design.
- **Facts renamed.** `CheatEngineRuntimePlatformInfo.SystemArchitecture` is `HostArchitecture` and
  `TargetPointerSize` is `TargetBitness`; `ProcessSnapshot.TargetArchitecture` is `Architecture` and
  `TargetPointerSize` is `Bitness`; `IMemoryReadContext.PointerSize` gives way to `Bitness` and the configured pointer
  size. The runtime snapshot groups its facts in `Version` and `Platform`.
- **Enum values.** Every public enum is an `int` enum with explicit values, and every outcome enum has `Unknown = 0`.
  This shifts the values of `ClientCapabilityEvidenceReasonCode`, `MemoryBatchWriteEffectState` (whose `Complete` is
  `Completed`) and `ValueScanSessionState` (whose `Disposed` is `Closed`).
- **Capabilities that were contracts.** Value scans, target allocations and instructions, which always reported
  `CapabilityUnavailable` before 1.0.0, are operational adapters, experimental (`CECLIENT5001` to `CECLIENT5003`).
- **Lua.** A Lua module or export name that the activation already reserved is refused with `OperationRejected`, and
  a generated module whose registration is refused throws the exception of its failure's kind.
- **Composition.** `CheatEngineClientOptions.AllowedTableRoots` is an `IList<string>` and `MemoryResourceLimits` is
  never `null`; neither has a setter. Configure the plugin's own files against `builder.PluginDirectory`, not
  `AppContext.BaseDirectory`, which is the folder of the .NET host inside Cheat Engine.
- **Fluent.** The builders are plain `readonly struct`s without equality, each bound once to the service that created
  it.
- **Template.** The Lua status global of a generated plugin is derived from its project name instead of the fixed
  `cheatengine_client_plugin_status`.

### Removed

- **Domains that no CheatEngine.SDK primitive backs:** timers, hotkeys, the debugger and breakpoints, the speed hack,
  target-memory and file hashing, DBVM, remote execution and DLL injection, with the event streams they used: the
  `Timers`, `Hotkeys`, `Debugger`, `Speed`, `Hashing`, `Dbvm`, `RemoteExecution` and `Events` namespaces, their
  `ICheatEngineClient` properties and their capability ids. None of them had an implementation: each always reported
  `CapabilityUnavailable`. `IProcessClient.Pause`, `ResumeExecution`, `GetPauseState`, `Create` and
  `AttachForeground`, and `IAssemblyClient.GetComment`, are removed for the same reason. The
  [ROADMAP](https://github.com/CheatEngineNet/CheatEngine.Client/blob/main/ROADMAP.md) lists what each one waits for.
- **Companion interfaces and shims:** `IMemoryBatchClient` (merged into `IMemoryClient`), `IDescribedLuaModule` (merged
  into `ILuaModule`), `ILocalProcessDiagnostics` (replaced by `IProcessClient.TryGetLocalProcesses`),
  `CheatEngineClientBuilder.AddMemoryCodec` and the implicit codec resolution, `ITableClient.GetCurrent` (use
  `GetSnapshot`), `ICheatEngineRuntime.GetSdkCapability` and `CheatEngineRuntimeSnapshot.SdkCapabilities`,
  `ILuaModuleLease.Epoch`, the `ILuaClient` overloads that took an `ILuaOperation<TResult>` interface, and the
  `MemoryStringReadRequest.Create` and `MemoryStringWriteRequest.CreateBounded` factories with their `WideCharacter`
  flag.
- **Public constructors that bypassed the contracts:** those of every Client exception, of the options validators
  (`CheatEngineClientOptionsSemanticValidator`, `ValidateCheatEngineClientOptions`, now internal), and the constructor
  and `BuildServiceProvider` of `CheatEnginePluginBuilder`.
- **Fluent helpers:** the static `Memory` entry class, `Using(memory)` and the overloads that took an `IMemoryClient`,
  `ReadUtf8`, `ReadUtf16`, `WriteUtf8` and `WriteUtf16` (use `ReadString` and `WriteString` with a
  `MemoryStringEncoding`), `ReadableExecutable()`, `WithProtectionFlags(string)` and the CheatEngine.SDK
  `AobScanOptions` of `AobScanBuilder.Options`.

### Security

- **Opt-ins.** `IUnsafeLuaClient` exists only after `EnableUnsafeLuaExecution()`, and `IAutoAssemblerClient` only
  after `EnableAutoAssemblerPatches()` (`CECLIENT5004`): without its opt-in, each capability reports `Unavailable` and
  every call is refused with `CapabilityUnavailable` and `NotStarted` before any Cheat Engine call (scenario Q44).
  Table files need an allowed root: without one they are `CapabilityUnavailable`, and a path outside every
  `AllowedTableRoots` entry is `OperationRejected`.
- **Registrations.** `IInspectionClient.TryRegisterSymbol` refuses a name that already resolves (a registered symbol,
  a module or an address expression) with `OperationRejected` and `NotStarted`, and a symbol or Lua module release
  never removes a name that another owner replaced.
- **SDK range.** A plugin that references `CheatEngine.SDK` 3.0 or later directly next to this Client fails to build
  with `CECLIENT017`. `CheatEngineClientAllowUnsupportedSdk=true` turns the error into a warning; such a plugin is
  unsupported and is expected to break at run time. A direct reference below 2.0.0 fails the restore with `NU1605`.
- **Log redaction (Q46).** No Client logging event carries an address, value, expression, path, script, message,
  exception or failure object, and a test rejects any event whose parameters could. `CheatEngineFailure.ToString()`
  returns only the kind, the operation and the host effect, and `LeaseReleaseOutcome.ToString()` only the kind and the
  effect. The host log provider writes an entry's message template and exception type name, never its placeholder
  values or exception message, unless `IncludeFormattedMessages` is set. A message built by string interpolation, such
  as `logger.LogInformation($"Read {address}")`, is its own template and carries its values: log constant structured
  templates or `LoggerMessage` methods. The `ceplugin` template logs a failure's kind, operation and host effect only.

### Deployment

- **CheatEngine.SDK 2.0.0.** The Client consumes exactly `CheatEngine.SDK` 2.0.0 and declares `[2.0.0, 3.0.0)`. The
  pin has a single source, `eng/CheatEngineSdk.props`, and the build refuses a pin outside 2.x or a prerelease pin
  (`CHEATENGINECLIENT9016`). The loaded CheatEngine.SDK passes the package gate when it has the same major at or above
  the pin. Core embeds the consumed package's version, source commit and content hash at build time and fails a build
  that cannot (`CHEATENGINECLIENT9050`). No experimental CheatEngine.SDK API is used.
- **CheatEngine.SDK values in the public API.** `Address`, `PointerSize`, `ModuleInfo` and the other descriptive
  CheatEngine.SDK 2.x values the charter lists appear in public signatures, so a CheatEngine.SDK 3.x means a Client 2.0,
  never a 1.x release.
- **Seven packages in lockstep.** The seven packages ship with one version, and each depends on the Client packages it
  builds on at exactly that version (`[X.Y.Z]`, `CHEATENGINECLIENT9019`). `CheatEngine.Client.Core` has no public API
  and is published only as a dependency of `CheatEngine.Client.Extensions.DependencyInjection`.
- **Versioning.** Every package is versioned by MinVer from `v*` tags: untagged builds are `1.0.0-alpha.0.<height>`,
  and the assembly version carries the major number only (`1.0.0.0`).
- **Package contents.** Every package embeds an SPDX 2.2 software bill of materials at
  `_manifest/spdx_2.2/manifest.spdx.json`, has its own description, and links the project, the license and this
  changelog; Source Link comes from the .NET SDK. The libraries are trim- and AOT-compatible and verify every
  referenced assembly, and a Native AOT probe publishes the whole Client graph and calls every Fluent member. Cheat
  Engine still loads the framework-dependent managed plugin folder.
- **Template.** The `ceplugin` template references the exact `CheatEngine.Client` version it was packed with and the
  pinned `CheatEngine.SDK`, is validated when it is built, and its instances restore with a lock file.
- **Release chain.** `release.yml` releases the seven packages from a `v*` tag: it verifies the tag and takes the
  release notes from this file, builds and tests the tag commit, stages the SBOMs and `SHA256SUMS`, attests the build
  provenance and the SBOMs, drafts the GitHub release, publishes to nuget.org through trusted publishing from the
  `nuget` environment after checking every package against `SHA256SUMS`, verifies the publication and only then
  publishes the GitHub release. A manual dispatch is a dry run.
- **Continuous integration.** CI builds and tests every change in Debug and Release with exactly .NET SDK 10.0.401 and
  locked restores, tests the packages that its Release leg packs (and that a release publishes) instead of packing
  them again, and requires the lock-file guard, the NuGet audit policy (high and critical advisories fail every
  build), formatting and the workflow security checks to pass before `CI / Gate` does.
