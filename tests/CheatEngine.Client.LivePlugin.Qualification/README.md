# CheatEngine.Client.LivePlugin.Qualification

## Context

The Client qualification harness: a real `CheatEngineClientPlugin`, composed like an application, whose Lua functions
run exact-host (C3/C4) Client scenarios one step at a time and return one JSON observation per call (schema
`cheatengine-client-qualification-observation/v0`). A future qualification runner is expected to load it into a
sandbox copy of Cheat Engine 7.7 and record those observations as evidence. CI compiles it through the solution so
that it never rots; it is **never packed, never a test module and never loaded by CI**.

> This project was prepared ahead of the Client's exact-host qualification evidence tooling. That tooling (a
> `docs/` folder and a bespoke `eng/qualification` runner) was rejected by the maintainer (2026-09-23): no top-level
> `docs/` folder and no custom PowerShell/JSON governance or evidence tooling under `eng/` in this repository. This
> harness is kept because it depends on neither — it only composes the public Client API — but the scenario plan,
> matrix and receipt schema it was designed against do not exist in this repository yet and need re-scoping before
> the harness is wired to anything.

## Why this project exists

A C1 success never counts as a C3 success (audit `analyses/20`). The Client needs a plugin that exercises its own public
API on the exact host, with the exact CI packages, to produce Client receipts: plugin lifecycle and rollback (Q05, Q06,
Q43), memory codecs (Q20, Q21, Q33), AOB scans (Q27–Q29), runtime and target facts (Q31, Q32), tables and symbols
(Q16.b, Q34), capabilities (Q44, Q45) and logs (Q46). The SDK harnesses cannot stand in for it: they do not go through
the Client.

## How it helps improve CheatEngine.Client

- **Client API only** (ADR-01). Every Cheat Engine interaction goes through `ICheatEngineClient` and its companions
  (`IPatternScanOutcomeClient`, `IMemoryBatchClient`). Cheat Engine-level setup (opening the target, allocating the scratch
  region, changing the pointer size, redefining a symbol, destroying a record) belongs to the scenario's driver, which a
  future qualification runner would generate from a scenario plan (not yet re-scoped; see the note above).
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
  gate so the SDK runner writes one manifest for any harness). A mutating function runs only when the process variable
  `CE_SDK_LIVE_PROBE_ACKNOWLEDGEMENT` holds the exact phrase, `CE_SDK_LIVE_PROBE_AUTHORIZATION_FILE` names a
  `ce77-live-probe-v1` manifest that is valid for at most 30 minutes and marks its target disposable, the host process is
  the pinned `cheatengine-x86_64.exe` 7.7.0.10621 (SHA-256 and file version), and the declared target is alive with the
  declared image hash. The runner writes the manifest for every scenario flagged `"authorization": true`.
- **Target check.** A mutating function also requires the process the Client observes to be exactly the authorized
  target; the gate never authorizes Cheat Engine itself.
- **Write guard** (`Harness/QualificationWriteGuard.cs`). Writes go only into the scratch region the driver allocated
  (`alloc(cheatengine_client_qualification_scratch,4096)` with `registersymbol`) and `target_declare` verified through
  the Client inspection API (a committed, private, writable region that holds 4096 bytes). Every written range must lie
  inside it; the original bytes are read first and restored. The partial-batch scenario writes one element at `0x10`
  on purpose, an address of the never-mapped first 64 KiB.
- **Fault switch** (`Harness/QualificationFaultSwitch.cs`). The SDK runner writes `liveprobe.fault.json`
  (`ce77-live-probe-fault-v1`) next to the plugin for a scenario with a `faultStage`, and removes it when an operator
  step says so. It is read once per enable and honored only when the gate allowed the run: `Configure`,
  `ModuleOnEnabled`, `ModuleOnDisabling`, `ResourceCleanup` or `ModuleOnDisablingAndResourceCleanup`.
- **Log sink** (`Harness/CapturingLoggerProvider.cs`). It records category, event id, level and message template of
  every event, never the formatted message, and counts the events whose formatted text carries a declared scenario
  value, a target address or the script marker (Q46).

## Lua functions

Every name starts with `cheatengine_client_qualification_`. A function marked **yes** changes the target or Cheat Engine
state and runs only behind the gate and the target check (`QualificationScenarios.RunMutating`).

| Function                                                 | Mutating                  | Scenarios          | Returns                                                                                              |
|----------------------------------------------------------|---------------------------|--------------------|------------------------------------------------------------------------------------------------------|
| `status()`                                               | no                        | Q05, Q06, Q40, Q43 | Gate and fault decisions, plugin id, epoch, activation count, assembly identities, bridge SHA-256, lifecycle ledger |
| `runtime()`                                              | no                        | Q31, Q32, Q45      | Process id first, then the Client's target architecture, pointer width, ABI and selection epoch      |
| `capabilities(probeOnly)`                                | when `probeOnly` is 0     | Q44, Q45           | Availability of every Client capability; with 0, one harmless call per family reported unavailable   |
| `target_declare(symbolName)`                             | yes                       | Q20, Q21, Q33, Q34, Q16.b | The verified scratch region, or why it was refused                                           |
| `aob(pattern, moduleName, maxResults, cancelAfterMs)`    | no                        | Q27, Q28, Q29, Q46 | Outcome, failure kind, bounded matches, host and copy metrics, managed allocations, exactness checks |
| `memory_roundtrip(kind, symbolName)`                     | yes                       | Q20, Q21, Q46      | Bytes, strings, 32/64-bit boundaries or an address above 4 GiB written, read back exactly, restored  |
| `memory_batch_partial(symbolName, invalidAddress)`       | yes                       | Q33, Q46           | Attempted, completed and failed index, effect state, read-back confirmation                          |
| `table_create(symbolName)`                               | yes                       | Q34                | The created record id                                                                                |
| `table_probe()`                                          | no                        | Q34                | Whether the remembered record id is refused or answered by another record                            |
| `symbol_register(name, symbolName)`                      | yes                       | Q16.b              | The Client symbol lease                                                                              |
| `symbol_release(name)`                                   | yes                       | Q16.b              | Whether the lease released its registration                                                          |
| `symbol_state(name)`                                     | no                        | Q16.b              | The lease and what the name resolves to now                                                          |
| `logs()`                                                 | no                        | Q46                | Captured templates and event ids, and the sensitive-data hit count; never a formatted message        |

`memory_roundtrip` kinds: `bytes-with-nul`, `utf8-multibyte`, `utf16-with-nul`, `int32-minus-one`, `uint32-max`,
`int64-limits`, `address-above-4gib`.

This Client version exposes no configured pointer size apart from the process width, so `runtime()` reports
`configuredPointerSize.exposedByClient: false`; the Q31 scenario records the configured size through its driver.

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
parsers. The scenario plan and evidence rules the harness serves are still to be re-scoped (see the note above).
