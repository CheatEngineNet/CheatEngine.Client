# Cheat Engine 7.7 x64 live-capability gates

## Purpose and promotion rule

This document is the release gate for advanced CheatEngine.Client capabilities. It is deliberately stricter than a build, unit test, package smoke test, or Native AOT probe: every listed operation crosses a Cheat Engine-owned Lua, ABI, callback, or ownership boundary whose real lifecycle must be observed in the supported host.

Every capability below starts as Unknown or Unavailable. A public interface, a completed implementation, an SDK binding, or an ordinary-CI result is not evidence that it may return Available. The only promotion rule is:

> A capability may return Available only after its complete domain scenario below has passed in Cheat Engine 7.7 x64, the required evidence has been captured and reviewed, and the result has been linked from the capability matrix. A partial result, another CE version or architecture, or a run without cleanup, disable, re-enable, and target replacement leaves the capability Unknown or Unavailable.

Run gates only against a local process the operator is authorised to control and that is disposable. Use a purpose-made test executable with known memory, code, file, and timing behaviour. Never use a production process, an online game, or a target that cannot be restored. The test must use an actual 64-bit Cheat Engine 7.7 host rather than an SDK fake or simulated Lua runtime.

The supported artifact remains a framework-dependent managed net10.0 plugin folder. Client libraries are analysed for trimming and Native AOT compatibility, and the AOT probe validates that analysis. Neither analysis nor the probe means Cheat Engine can load a Native AOT plugin DLL. Exercise the managed plugin with its .deps.json, .runtimeconfig.json, Client and SDK assemblies, and native SDK bridge deployed together.

## Common protocol and evidence

Capture this bundle for every domain:

1. **Host:** CE version/build, x64, Windows version, Client/SDK package or commit versions, plugin hash, timestamp.
2. **Target:** fixture executable path/hash, PID, bitness, selection epoch, and confirmation it is local, authorised, and disposable. Do not log sensitive target-memory contents.
3. **Operation:** request metadata, copied Client result, stable CheatEngineFailure on errors, and host-side evidence (log, UI screenshot, process/module or memory-map observation) of the intended effect.
4. **Lifecycle:** enable, success, intentional failure, cleanup, disable, fresh re-enable, and target replacement; record Client epoch and capability observation at every boundary.
5. **Negative checks:** rejected work makes no host mutation; stale leases/callbacks never act in a new epoch; no callback blocks a Cheat Engine or Lua thread.

The raw log remains access controlled; the attached review summary is redacted. It names the exact host, SDK, and Client versions. A changed SDK ABI or CE version requires a new relevant gate run; older evidence is not inherited.

| Required phase | Assertion |
|---|---|
| Initial state | The service is resolvable, returns Unknown/Unavailable, and makes no speculative mutation. |
| Success | Only copied Client data or activation-scoped Client leases return; the host effect is independently observed. |
| Expected failure | Invalid input, unavailable primitive, and host error become stable failures; no partial resource, callback, patch, or allocation survives. |
| Cleanup | A lease disposes once in the documented order and the host returns to its recorded baseline. |
| Disable | Admission closes before SDK/Lua teardown; Client resources are neutralised and released while the SDK context is valid. |
| Re-enable | A new Client epoch has only new resources; old leases, callbacks, and subscriptions are rejected or inert. |
| Target change | Old selection-bound resources are reclaimed/invalidated before a new PID is used and cannot act on it. |

## Allocations — Allocations

**Preconditions.** Select the disposable local target, record PID and selection epoch, and choose a positive bounded size with a documented protection mode. The allocation must not enter a cache or pool. Start with the Allocations capability observed as Unknown/Unavailable.

**Success.** Allocate(TargetAllocationRequest) returns ITargetMemoryLease with a non-zero address, requested size, and current selection epoch. Verify the range belongs to the selected PID and, where supported, write/read one bounded fixture value. No SDK owner or raw handle may escape in its result.

**Failure, cleanup, and lifecycle.** Exercise zero, oversized, unsupported, and injected SDK allocation failures; each must yield CapabilityUnavailable or a classified failure without a remote range leak. Dispose a successful lease twice and prove deallocation occurs at most once. Selection change and disable release the old range before detachment; an old lease cannot touch the replacement target. Re-enable then allocates a fresh range in a new epoch.

**Evidence.** Capture address, size, PID, old/new epochs, allocation/deallocation statuses, memory-map evidence, and no-leak proof after failure, disable, and target replacement.

## Assembly and Auto Assembler — Assembly

**Preconditions.** Use a known instruction sequence in the fixture. Capture original bounded byte and disassembly snapshots. A test Auto Assembler script is reversible, has a valid [DISABLE] path, contains neither targetSelf nor host pointers, and touches only the fixture target. Start Unknown/Unavailable.

**Success.** Verify copied disassembly, instruction size, previous-instruction lookup, comments, and a one-instruction assembly result. Apply the reversible script and obtain IAutoAssemblerPatchLease; independently observe its one intended effect. Dispose it and prove the SDK executes retained disableInfo once and restores original bytes.

**Failure, cleanup, and lifecycle.** Test malformed assembly, invalid address, and a script failing part way through. Each returns a classified error and leaves original bytes intact; a partially-created patch rolls back. Double disposal cannot execute [DISABLE] twice. Disable and target change restore a live patch before Lua detaches; an old lease cannot disable a patch from a new epoch or PID. Re-enable uses a newly created lease only.

**Evidence.** Capture script hash, original/patched/restored byte hashes, copied instruction values, disableInfo status, PID/epochs, failures, and host disassembly evidence.

## Remote execution and DLL injection — RemoteExecution

**Preconditions.** Select the authorised disposable x64 target. Supply a trusted target-compatible test DLL using an existing absolute path. Calls use a strictly positive finite timeout; parameters flow through a Client-owned allocation lease, never a caller-supplied host pointer. Begin Unknown/Unavailable.

**Success.** Demonstrate deterministic injection and a harmless exported fixture action with a bounded copied result. Verify parameter allocation belongs to the PID, the action honours its timeout, and allocation is released on both normal completion and remote error. A public result retains no remote pointer.

**Failure, cleanup, and lifecycle.** Reject relative, missing, inaccessible, and wrong-bitness DLL paths; zero or negative timeout; bad parameters; remote exception; and timeout. None may leak an allocation, pending callback, or unrelated mutation. If CE cannot safely unload an injected DLL, state that limitation and use a new fixture process for remaining phases instead of claiming unload semantics. Disable while a call is pending, re-enable, and prove old state cannot complete into the new epoch. On target change, release old allocations/call state before new PID use.

**Evidence.** Capture canonical DLL path/hash, PID/epochs, timeout, allocation lifecycle, exit/result status, host logs, and process-module evidence.

## Debugger and breakpoints — Debugger

**Preconditions.** Use a debugger-safe fixture with a known breakpoint address and controlled trigger. An IBreakpointLease callback receives a copied event and returns BreakpointDisposition synchronously; it never awaits an async consumer or blocks Lua/CE. Any event stream has positive capacity and named overflow policy. Begin Unknown/Unavailable.

**Success.** Trigger once and verify copied address/register/context values and immediate disposition against fixture behaviour. Saturate an optional stream to prove its selected overflow policy and observable drop count without blocking the producer.

**Failure, cleanup, and lifecycle.** Invalid/unmapped registration and a handler exception leave no armed breakpoint or uncaught host exception. Dispose, trigger again, and prove no admission. On disable: close admission, neutralise the SDK callback, complete streams, release the lease, then detach Lua. Re-enable has a new scope; a retained old closure/subscription is inert. Changing target before trigger never delivers old breakpoint work to the new PID.

**Evidence.** Capture breakpoint IDs, event sequence, disposition, stream capacity/drop count, callback-thread proof, PID/epochs, and removal proof.

## Hotkeys — Hotkeys

**Preconditions.** Choose a non-conflicting reversible fixture chord in a controlled environment. A handler receives only copied events. An asynchronous projection always supplies positive-capacity EventStreamOptions and an explicit overflow policy (DropOldest default, DropNewest, or FailSubscription). Begin Unknown/Unavailable.

**Success.** Register IHotkeyLease, trigger the chord through the host, and prove exactly one copied event reaches the active epoch. Deliberately fill a stream and record loss behaviour without blocking the Lua/registration thread.

**Failure, cleanup, and lifecycle.** A duplicate/conflicting chord and handler failure leave no partial registration or host exception. Disposal prevents the chord reaching the plugin. Disable closes admission, neutralises callbacks, completes subscribers, releases host hotkey, then permits Lua detach. A fresh re-enable lease works while the former delegate/stream remains inert. Changing target must not retain old selection-bound state or direct work to it.

**Evidence.** Capture chord, registration/removal statuses, event/loss counts, epochs, and host UI/log proof.

## Timers — Timers

**Preconditions.** Use a positive bounded interval and callbacks that record copied timestamps only. Every async consumer uses positive-capacity EventStreamOptions; a timer callback never waits for a consumer or calls a blocking dispatcher. Begin Unknown/Unavailable.

**Success.** Acquire ITimerLease, observe a bounded number of ticks within documented tolerance, and prove timer and stream are activation-scoped. Fill the stream to verify overflow/drop accounting and a non-blocked CE/Lua thread.

**Failure, cleanup, and lifecycle.** Zero, negative, unsupported interval and injected host creation failure leave no timer. After disposal, wait more than two intervals and prove no admission. Disable closes admission, neutralises callback, completes stream, and frees the timer before Lua teardown. Re-enable creates a new timer/scope; an old lease cannot stop or receive it. A target replacement must not cause a tick to use an old selection epoch.

**Evidence.** Capture requested/observed intervals, tick/event/drop counts, timer IDs, callback-thread proof, PID/epochs, and final stop evidence.

## Speed — Speed

**Preconditions.** Record the host baseline speed and use a disposable local fixture. Inputs are finite and strictly positive. Speed is host-wide rather than a target allocation lease, so the gate explicitly restores baseline; Client must not silently initialise or reset it. Begin Unknown/Unavailable.

**Success.** Read a copied baseline, set a known finite positive multiplier, read it back, and verify the fixture's timing change within a documented tolerance. Restore the exact recorded baseline as the test cleanup action.

**Failure, cleanup, and lifecycle.** Reject zero, negative, NaN, and infinities before host mutation. A host error leaves the last valid value intact. At disable, re-enable, and target change, observe/record state without assuming per-process ownership, then restore the pre-test setting before finishing.

**Evidence.** Capture baseline/request/read-back values, timing samples/tolerance, validation/host failures, host setting evidence, PID/epochs, and baseline-restoration proof.

## Hashing — Hashing

**Preconditions.** Test target-memory hashing and file hashing separately. For memory, select the local fixture and a bounded readable known-byte range. For files, use an authorised local fixture with a known digest. Begin Unknown/Unavailable.

**Success.** Hash each input independently and compare copied digests with external known values. Define any same-content equivalence only under the documented algorithm/encoding. Neither operation mutates target memory or a file.

**Failure, cleanup, and lifecycle.** Exercise unreadable/out-of-range memory, missing/unauthorised file, invalid range/algorithm, and selection change during memory hashing. Each returns a stable failure without a borrowed buffer, open file handle, or hashing the replacement PID. The service has no host lease, but disable/re-enable safely stop admission and a prior-epoch result is never reported as current.

**Evidence.** Capture algorithm, bounded range metadata or canonical fixture path/hash, expected/actual digest, errors, resource checks, PID/epochs, and redacted no-content-logging proof.

## DBVM observation, initialisation, and watches — Dbvm

**Preconditions.** Use only a dedicated, authorised VM test environment whose hardware/OS support is understood by the operator. Observe DBVM state first: Client never initialises DBVM implicitly. Explicit initialisation requires a separate operator-approved rollback/VM-reset plan. Begin Unknown/Unavailable.

**Success.** Prove state observation is side-effect free. Only when explicit initialisation is authorised, record pre/post state and install IDbvmWatchLease on a harmless fixture range. Verify copied events, bounded stream behaviour, and immediate non-blocking callback handling.

**Failure, cleanup, and lifecycle.** Unsupported hardware, denied init, invalid range, and callback failure may not initialise DBVM or leave a watch active. Dispose watch and prove removal. If no safe in-session DBVM shutdown exists, document it, use the approved VM reset, and do not claim a Client deinitialisation guarantee. Disable closes admission, neutralises callbacks, completes streams, and removes watches before Lua detach. Re-enable creates fresh watches; selection change removes old watches before a new PID.

**Evidence.** Capture observation/explicit-init decision, hardware/VM facts, watch IDs/ranges, stream/drop counts, lifecycle logs, reset proof where needed, and PID/epochs.

## Value scanning — ValueScanning

**Preconditions.** Select a fixture exposing known mutable values in a bounded documented region. SDK must have a production factory that creates and owns MemScan plus FoundList; Client must not hand-construct or adopt an unproven owner. Begin Unknown/Unavailable even if the public state machine exists.

**Success.** Create one session, perform first scan, mutate the fixture, perform next scan, and read bounded copied result pages. Prove factory construction order is parent scan then child found-list. Confirm Client metadata exposes no LuaState, LuaRef, CEObject, Owned<T>, borrowed Lua value, or raw pointer.

**Failure, cleanup, and lifecycle.** Inject child creation failure after parent creation and prove parent rollback. Exercise invalid requests, cancellation before admission, empty/no-result scan, and failure during next scan. Dispose and prove child is destroyed before parent; disposal is idempotent and no owner remains after disable or selection change. Re-enable creates a new session; retained old page/session cannot scan or read new epoch data.

**Evidence.** Capture request/result counts without sensitive values, parent/child create-destroy order, page bounds, cancellation/failure statuses, PID/epochs, CE scan UI/log evidence, and leak checks.

## Recording the decision

Attach the evidence bundle to the release issue or capability matrix, with exact host/SDK/Client versions and a pass/fail result for every phase. The review must affirm all four points:

1. The managed plugin artifact ran in Cheat Engine 7.7 x64.
2. Success, expected failure, cleanup, disable, re-enable, and target replacement passed.
3. Ownership, epochs, callback admission, and Lua detachment were observed rather than inferred.
4. The high-level boundary holds: normal Client code sees copied data and leases only, never SDK/Lua handles or raw pointers.

Only four affirmative answers and the domain-specific evidence permit Available. Otherwise keep Unknown/Unavailable, retain the published contract if any, and exclude the feature from unguarded examples.
