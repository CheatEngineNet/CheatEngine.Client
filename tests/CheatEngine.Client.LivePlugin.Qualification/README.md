# CheatEngine.Client.LivePlugin.Qualification

## Context

The Client qualification harness: a real `CheatEngineClientPlugin`, composed like an application, whose Lua functions
run exact-host (C3/C4) Client scenarios one step at a time and return one JSON observation per call (schema
`cheatengine-client-qualification-observation/v0`). The live qualification runner of `CheatEngine.Client.Tests`
(section [Live qualification](../CheatEngine.Client.Tests/README.md#live-qualification)) loads it into a sandboxed copy
of Cheat Engine 7.7 and turns those observations into receipts. CI compiles it through the solution so that it never
rots; it is **never packed, never a test module and never loaded by CI**.

## Why this project exists

A C1 success never counts as a C3 success (audit `analyses/20`). The Client needs a plugin that exercises its own public
API on the exact host, with the exact CI packages, to produce Client receipts: plugin lifecycle and rollback (Q05, Q06,
Q43), worker admission (Q19), memory codecs (Q20, Q21, Q33), value scans (Q25, Q26), AOB scans (Q27–Q29), allocations
(Q30.a, Q30.b), runtime, target and instruction facts (Q31, Q32), tables and symbols (Q16.b, Q34), Auto Assembler
patches (Q35), capabilities (Q44, Q45), logs (Q46) and the Lua marshalling rules of CRIT-07. The SDK harnesses cannot
stand in for it: they do not go through the Client.

## How it helps improve CheatEngine.Client

- **Client API only** (ADR-01). Every Cheat Engine interaction goes through `ICheatEngineClient` (its
  `IPatternScanner.ScanDetailed` included) and the services the activation registers. Cheat Engine-level setup
  (opening the target, allocating the scratch region, changing the pointer size, redefining a symbol, destroying a
  record) belongs to the session's driver, which the runner generates from its session plan.
- **Honest observations.** An observation is bounded and redacted by construction: failures are written as their kind,
  operation and host effect only, address lists as their count and first and last eight entries, and any text that
  looks like a local path is replaced. The harness checks its own criteria where the Client API allows it (for example
  `checks.filterExact` of a module scan compares it with the unfiltered scan) and records the raw facts next to them.
- **Lifecycle record.** Cheat Engine never unloads a managed plugin, so the harness keeps a bounded ledger of every
  enable, module stage, rollback and cleanup (stage names and exception types only) that `status()` reports after a
  re-enable.

The harness files that do not touch Lua (`Harness/`) are compiled into
[`CheatEngine.Client.LivePlugin.Qualification.Tests`](../CheatEngine.Client.LivePlugin.Qualification.Tests/README.md)
and tested there.

## Safety boundary

- **Qualification gate** (`Harness/QualificationAuthorization.cs`, fail-closed, re-implemented from the SDK LiveProbe
  gate, so one runner-written manifest authorizes any harness). A mutating function runs only when the process variable
  `CE_SDK_LIVE_PROBE_ACKNOWLEDGEMENT` holds the exact phrase, `CE_SDK_LIVE_PROBE_AUTHORIZATION_FILE` names a
  `ce77-live-probe-v1` manifest that is valid for at most 30 minutes and marks its target disposable, the host process is
  the pinned `cheatengine-x86_64.exe` 7.7.0.10621 (SHA-256 and file version), and the declared target is alive with the
  declared image hash. The runner's `AuthorizationManifestWriter` writes that manifest for every session.
- **Target check.** A mutating function also requires the process the Client observes to be exactly the authorized
  target; the gate never authorizes Cheat Engine itself. Two narrower scopes exist (`MutationScope`), still behind the
  gate: `OwnedResource` only releases a lease the harness created on the authorized target, after a target change too,
  because what the Client does then (refuse to free anything in another process) is what S3 observes; and
  `FileAsProcessTarget` runs only while Cheat Engine targets a file opened as a process, where no process exists, to
  check that the Client creates no allocation or scan session there (anything created by mistake is released at once
  and reported).
- **Write guard** (`Harness/QualificationWriteGuard.cs`). Writes go only into the scratch region the driver allocated
  (`alloc(cheatengine_client_qualification_scratch,4096)` with `registersymbol`) and `target_declare` verified through
  the Client inspection API (a committed, private, writable region that holds 4096 bytes). Every written range must lie
  inside it; the original bytes are read first and restored (the value-scan marker slot when its session is released,
  in the authorized target only). The partial-batch scenario writes one element at `0x10` on purpose, an address of
  the never-mapped first 64 KiB. Allocations and Auto Assembler patches write nothing through the harness: Cheat Engine
  allocates their memory in the authorized target, and the harness releases them.
- **Fault switch** (`Harness/QualificationFaultSwitch.cs`). The runner writes `liveprobe.fault.json`
  (`ce77-live-probe-fault-v1`) next to the plugin for a session with a fault stage, and removes it when the session plan
  says so. It is read once per enable and honored only when the gate allowed the run: `Configure`,
  `ModuleOnEnabled`, `ModuleOnDisabling`, `ResourceCleanup` or `ModuleOnDisablingAndResourceCleanup`.
- **Log sink** (`Harness/CapturingLoggerProvider.cs`). It records category, event id, level and message template of
  every event (Debug included), never the formatted message, and counts the events whose formatted text carries a
  declared scenario value, a target address or the script marker (Q46). The plugin also composes the Hosting host log
  provider (`AddCheatEngineHostLog`, message templates only), so the Cheat Engine debug output that Q46 reads carries
  the templates of the events the host log admits (`Information` and above by default).
- **Session inputs** (`Harness/QualificationInputs.cs`). The runner's `CECLIENT_QUALIFICATION_*` variables are honored
  only when the gate allowed the run: `CECLIENT_QUALIFICATION_ENABLE_AA=1` composes `EnableAutoAssemblerPatches()` (Q35;
  without it `aa_patch` is refused and Q44 observes the policy refusal), `CECLIENT_QUALIFICATION_TABLE_ROOT` is the one
  allowed table root (Q34), and `CECLIENT_QUALIFICATION_LIFECYCLE_FILE` names the lifecycle receipt sink
  (`Harness/QualificationLifecycleSink.cs`, Q43): every lifecycle record entry and the template of every captured log
  event is appended there, so the runner reads what a disable did after the last Lua call, the operator's or the one at
  `closeCE`. Each enable starts with its `configure` entry, which names the fault the switch selected.

## Lua functions

Every name starts with `cheatengine_client_qualification_`. A function marked **yes** changes the target or Cheat Engine
state and runs only behind the gate and the target check (`QualificationScenarios.RunMutating`).

| Function                                                 | Mutating                  | Scenarios          | Returns                                                                                              |
|----------------------------------------------------------|---------------------------|--------------------|------------------------------------------------------------------------------------------------------|
| `status()`                                               | no                        | Q05, Q06, Q40, Q43 | Gate and fault decisions, plugin id, epoch, activation count, assembly identities, bridge SHA-256, lifecycle ledger |
| `runtime()`                                              | no                        | Q31, Q32, Q45      | Process id first; backend, architecture, bitness and configured pointer size; host version, OS and Cheat Engine bitness |
| `capabilities(probeOnly)`                                | no                        | Q44, Q45           | State and evidence gates of every Client capability; with 0, the policy refusal of each opt-in capability |
| `target_declare(symbolName)`                             | yes                       | Q20, Q21, Q33, Q34, Q16.b | The verified scratch region, or why it was refused                                           |
| `aob(pattern, moduleName, maxResults, cancelAfterMs)`    | no                        | Q27, Q28, Q29, Q46 | Route, scope, host outcome, route reason, failure kind, bounded matches, metrics, zero-result and exactness checks |
| `memory_roundtrip(kind, symbolName)`                     | yes                       | Q20, Q21, Q46      | Bytes, strings, 32/64-bit boundaries or an address above 4 GiB written, read back exactly, restored  |
| `memory_batch_partial(symbolName, invalidAddress)`       | yes                       | Q33, Q46           | Requested, completed and failed index, effect state, read-back confirmation                          |
| `table_create(symbolName)`                               | yes                       | Q34                | The created record id                                                                                |
| `table_probe()`                                          | no                        | Q34                | Whether the remembered record id is refused or answered by another record                            |
| `symbol_register(name, symbolName)`                      | yes                       | Q16.b              | The Client symbol lease                                                                              |
| `symbol_release(name)`                                   | yes                       | Q16.b              | Whether the lease released its registration                                                          |
| `symbol_state(name)`                                     | no                        | Q16.b              | The lease and what the name resolves to now                                                          |
| `logs()`                                                 | no                        | Q46                | Captured templates and event ids, and the sensitive-data hit count; never a formatted message        |
| `value_scan(action, symbolName, value)`                  | yes, except `state`       | Q25, Q26           | Session state, result count and whether the scratch marker is found; the decimal tolerance cases     |
| `allocation(action, name, size)`                         | yes, except `state`       | Q30.a, Q30.b       | The lease, its region through the inspection API, and every release outcome                          |
| `instructions(symbolName)`                               | yes                       | Q32                | Assembled bytes of a fixed list and both jump encodings, and a disassembly round trip in the scratch |
| `aa_patch(action, variant)`                              | yes, except `state`       | Q35, Q44           | Check result, the patch lease or the failure, whether its symbol resolves, and its release outcome   |
| `worker_admission(action)`                               | `start`                   | Q19                | `"pending":true`, then the marshalled call and the refused direct registration from the worker      |
| `table_save(fileName, outsideRoot)`                      | yes                       | Q34                | Whether the table was saved below the table root, or refused outside it                              |
| `table_load(fileName)`                                   | yes                       | Q34                | Whether the table was loaded (which ends every earlier record id)                                    |
| `integer_echo(value)`                                    | no                        | CRIT-07            | The integer CheatEngine.SDK marshalled                                                               |
| `address_echo(address)`                                  | no                        | CRIT-07            | The address CheatEngine.SDK marshalled                                                               |

`memory_roundtrip` kinds: `bytes-with-nul`, `utf8-multibyte`, `utf16-with-nul`, `int32-minus-one`, `uint32-max`,
`int64-limits`, `address-above-4gib`.

`runtime()` reports the pointer size Cheat Engine is configured with as the Client exposes it
(`configuredPointerSize.exposedByClient: true`, with `bytes` and `differsFromBitness`): the Q31 driver changes it with
`setPointerSize` and restores it, and the harness never derives it from the target bitness. A fact the Client reports as
unknown is written as `null`.

`aob()` names the route that ran (`route.scope`, `route.hostOutcome`, `route.reason`,
`route.targetIdentityVerified`) and every metric of `PatternScanMetrics`. A global scan that finds nothing is
indeterminate (`checks.globalZeroIsIndeterminate`: `IndeterminateHostResult` with the host outcome `NoResult`); a
bounded scan that finds nothing is a factual empty result (`checks.boundedZeroIsNoMatches`). Truncation is proven by the
Client's own counts (`checks.truncationExplicit`), and the harness holds no copy of the Client's copy cap.

`value_scan` actions: `first` and `next` write the int32 marker `value` into its scratch slot and scan the scratch region
for it, `reset`, `state`, `release` (also after a target change), `decimals` (Q25: the probe 3.14159 written as a float
and a double must be found by the texts of `FromSingle`/`FromDouble` with 5, 2 and 0 decimals and must not be found by
3.2 or 3.15, under either rule that fits Cheat Engine's `rtRounded` documentation; the 3-decimal text 3.142 is recorded
without an expectation, because ordinary rounding finds the probe with it and the documented range, which reaches only
half a unit above the text, does not) and `create-unidentified` (a file opened as a process). `allocation` actions: `allocate`, `state`, `release` and `allocate-unidentified`. `aa_patch` actions: `check`,
`apply` (variant `benign`: an allocation and a registered symbol that its `[DISABLE]` section unregisters and frees;
variant `failing`: a write to an undefined label), `state` and `release`. The driver selects every target through
Cheat Engine itself (`openProcess`), never through the Client, so a patch always applies to a process that Cheat Engine's
own selection chose.

`worker_admission` never blocks Cheat Engine's main thread: `start` returns at once, and the worker's marshalled call
completes while the driver polls `result`. The probe module of the direct registration
(`QualificationWorkerProbe.cs`) is never added to the activation; its global must stay absent.

`integer_echo` and `address_echo` exist for CRIT-07: CheatEngine.SDK's marshallers accept an integer exactly and refuse a
float from 2^53 on with a Lua error, which the driver records.

`capabilities(0)` is the Q44 observation: without `EnableAutoAssemblerPatches` the activation registers no
`IAutoAssemblerClient` (and without `EnableUnsafeLuaExecution` no `IUnsafeLuaClient`), so no call can reach Cheat Engine,
and the capability reports a `Missing` policy gate. The harness only reads the capability evidence and resolves the
service; it makes no other call. This departs from the plan's Q44, which asked to observe a `NotStarted` refusal with no
host call: without the opt-in there is no client to call, so the Client's own refusal (a policy refusal with
`NotStarted`, before any port call, when a composition without the opt-in reaches `AutoAssemblerClient`) is proven at
C1 only, by `AutoAssemblerClientTests` in `CheatEngine.Client.Core.Tests`. The live receipts state what they observed:
the missing service and the `Missing` policy gate.

## Run

CI builds the project with the solution; nothing runs it. To see the complete deployment closure the Hosting targets
produce from this source graph:

```powershell
dotnet build .\tests\CheatEngine.Client.LivePlugin.Qualification\CheatEngine.Client.LivePlugin.Qualification.csproj -c Release -p:CheatEnginePluginOutputPath=<folder>
```

A host run never uses that build. The sandboxed runner of `CheatEngine.Client.Tests` (section
[Live qualification](../CheatEngine.Client.Tests/README.md#live-qualification)) compiles the same sources again outside
the repository against the exact packed Client packages and the pinned CheatEngine.SDK 2.0.0 from nuget.org, and loads
them into a private copy of Cheat Engine, only on an explicit opt-in and never in CI. It compiles `QualificationAuthorization`
and `QualificationFaultSwitch` in, so the manifest and fault switch it writes are proven against this harness's own
parsers.
