# CheatEngine.Client.LivePlugin.Coexistence

This is an opt-in manual fixture for the Client-facing part of multi-plugin observation. It builds three managed plugin
assemblies with the public `CheatEngineClientPlugin` composition path, generated Lua modules, and separate complete
output directories:

- `PluginA` and `PluginB` are the positive, distinct-export pair.
- `PluginCollision` exports the exact same Lua name as Plugin A and is an expected-failure contender, not a third
  successful plugin.

It aligns with the evidence protocol introduced by [SDK PR #56](https://github.com/CheatEngineNet/CheatEngine.SDK/pull/56):
record the identities the exact Cheat Engine host actually chose; do not manufacture an `AssemblyLoadContext`, infer
loader isolation from separate Client DI providers, or treat a successful build as a live result.

## What the fixture observes

Every plugin enable creates a fresh Client provider and one activation scope. Each fixture module records the SDK
plugin ID, its local Client epoch, and options count. The identity Lua global reports copied assembly/load-context
identity and the ping global is monotonic. The identity string includes the plugin, Client Hosting, and SDK Hosting
assemblies plus their module-version IDs and load-context facts.

Plugin A also exposes an exact collision marker, a target-observation function, and an explicitly opt-in retained-owner
probe. Plugin B exposes an independent target-observation function. Both target functions call only
`IProcessClient.TryRefresh`: they observe the current selection but never select, create, pause, or mutate a process.
The owner probe is inert until the operator calls it on an authorized disposable target. With the currently released
Client/SDK tuple it returns `CapabilityUnavailable` and no lease; this is a blocker record, not a passing owner test.
When a future qualified Client/SDK tuple provides a real allocation owner, the same probe retains a 16-byte lease so a
subsequent observed target change can prove the old lease was invalidated before anything can act on the new target.

The fixture never creates a loader policy or process-wide synchronization mechanism. It does not add a target or
memory operation automatically. Side-by-side SDK packages, shared Lua/CE state, worker concurrency, target switching,
and retained-owner behavior remain `Specified_Not_Executed` until their exact controlled-host transcripts are attached.

## Package and host boundary

The fixture references the changed Client Hosting/source-generator graph so a source build exercises the new Client
contract. It still references the released `CheatEngine.SDK` 1.0.0 package directly, which supplies the SDK entry-point
generator and bridge assets. The default `CoexistenceSdkPackageVersion` follows the repository pin in
`eng/CheatEngineSdk.props`. It can be overridden per fixture project only when an operator has an exact candidate SDK
package source and tuple to qualify. A version other than the pin never rewrites this fixture's committed
`packages.lock.json`: the restore records the candidate closure in `coexistence-candidate.packages.lock.json` under the
project's `obj` folder instead, and lock files stay enabled because disabling them next to a committed lock file fails
the restore (NU1005). It is not an invitation to invent or float package versions.

The source fixture is not a replacement for a clean Client package consumer. The package-consumer gate is the C#
`PackageConsumptionSmokeTests` suite, which consumes the immutable package directory supplied through
`CHEATENGINE_CLIENT_PACKAGE_SOURCE`. The coexistence fixture skips direct-package profile validation because its
Client graph is a project reference; that distinction must remain explicit in any live qualification receipt.

The resolved SDK package is not evidence that it contains the later SDK PR #56 source merge, nor is that merge a
published-package or live-host qualification. Before a real run, identify the exact qualified Client/SDK package tuple,
record package and DLL SHA-256 hashes, and keep the complete dependency closure for each plugin in its own directory.
Never copy DLLs from one output into another, reuse a bundle directory, or infer the selected SDK version from a
filename.

Use the controlled Windows x64 Cheat Engine 7.7 profile. Before loading either plugin, record the Cheat Engine binary
version/architecture/SHA-256, .NET and `hostfxr` policy, source or package identities, complete output paths and
hashes, timestamp/operator, and the full diagnostic/Lua transcript. A missing field means an unqualified manual
observation, not a portable hosting claim.

## Prepare isolated bundles and a receipt

The current checkout does not include an automated bundle-preparation runner. Do not infer that a live fixture or a
receipt exists from this document. For a future exact SDK package tuple, prepare three disjoint output directories
with an approved harness, record the package and assembly hashes, and retain the complete dependency closure and
host transcript before loading Cheat Engine.

Different requested package versions only make a side-by-side live run eligible. The receipt's assembly identities,
package content hashes, full closures, and host transcript must still establish what the exact Cheat Engine loader did.

## Controlled-host manual protocol

Use only the controlled Windows x64 Cheat Engine 7.7 profile and two local, authorised, disposable target fixtures.
Before loading a plugin, supplement the generated receipt with the host EXE version/architecture/SHA-256, .NET and
`hostfxr` policy, source/package identities, timestamp/operator, target profile paths/hashes/PIDs, and complete
DebugView/Lua transcripts. A missing field makes the result an unqualified manual observation.

In the controlled host's **Edit > Settings > Plugins** UI, add the Plugin A and B DLLs from their own bundle directories
without changing the installed host configuration. Enable A then B. Record the result of each command in the Lua Engine:

```lua
print(cheatengine_client_coexistence_a_identity())
print(cheatengine_client_coexistence_b_identity())
print(cheatengine_client_coexistence_a_ping())
print(cheatengine_client_coexistence_b_ping())
assert(cheatengine_client_coexistence_a_collision() == "CollisionOwner=A")
```

Disable A and record that its globals are absent while B's identity and ping remain callable. Use direct Lua assertions
and preserve their output:

```lua
assert(cheatengine_client_coexistence_a_identity == nil)
assert(cheatengine_client_coexistence_a_ping == nil)
assert(cheatengine_client_coexistence_a_collision == nil)
assert(type(cheatengine_client_coexistence_b_identity) == "function")
assert(type(cheatengine_client_coexistence_b_ping) == "function")
print(cheatengine_client_coexistence_b_identity())
print(cheatengine_client_coexistence_b_ping())
```

Re-enable A. Before introducing `PluginCollision`, prove A's owner marker again, then add and attempt to enable the
collision DLL. The generated Client module must refuse to replace the non-`nil` A global. Record the host-visible enable
failure, then prove the established plugin survived untouched:

```lua
assert(cheatengine_client_coexistence_a_collision() == "CollisionOwner=A")
assert(type(cheatengine_client_coexistence_a_identity) == "function")
assert(type(cheatengine_client_coexistence_a_ping) == "function")
```

### Third-party replacement survives disable (Q16)

Run this step once the collision attempt is recorded, with A and B enabled. The third party is an explicit operator
script: a deliberate protocol step that replaces one of A's globals after A registered it, not a repair. In the Lua
Engine:

```lua
cheatengine_client_coexistence_q16_third_party = function() return "ThirdParty=Q16" end
cheatengine_client_coexistence_a_ping = cheatengine_client_coexistence_q16_third_party
```

Disable A only, then record:

```lua
assert(cheatengine_client_coexistence_a_ping == cheatengine_client_coexistence_q16_third_party)
assert(cheatengine_client_coexistence_a_ping() == "ThirdParty=Q16")
assert(cheatengine_client_coexistence_a_identity == nil)
assert(cheatengine_client_coexistence_a_collision == nil)
assert(type(cheatengine_client_coexistence_b_identity) == "function")
print(cheatengine_client_coexistence_b_ping())
```

Expected result: A's disable removes the globals A still owns, leaves the third-party value under
`cheatengine_client_coexistence_a_ping` in place, and does not touch B. A Client whose generated modules still release
through the legacy SDK 1.0.0 unregistration helper writes `nil` there, so the first assertion fails. A failing assertion
is recorded as `Failed` and is never repaired.

The operator then removes the third party. While it holds the name, the generated preflight refuses to enable A again,
because the global is defined; that refusal is expected and is not the result of this step:

```lua
cheatengine_client_coexistence_a_ping = nil
cheatengine_client_coexistence_q16_third_party = nil
```

Re-enable A and prove its marker again:

```lua
assert(cheatengine_client_coexistence_a_collision() == "CollisionOwner=A")
assert(type(cheatengine_client_coexistence_a_ping) == "function")
print(cheatengine_client_coexistence_a_ping())
```

This step observes only the Lua-visible effect on the exact host. It does not observe the `Replaced` status of A's
release outcome (`IOwnershipAwareLuaModule.LastReleaseOutcome`): that status is C1 evidence of the generator EndToEnd
tests, not host evidence.

Disable the failed contender if the host exposes it as enabled, then disable B and finally A, recording every lifecycle
result. Stop and retain the failure evidence if a positive plugin cannot load or enable, an expected collision does not
fail, a disabled plugin global remains (other than the operator's third-party value of the Q16 step), or a surviving
plugin stops answering. Do not repair a failed observation by assigning Lua globals manually.

### Target switch and retained-owner extension

Run this extension only after the positive/collision sequence and only against two purpose-built disposable targets.
Select target A in the controlled host, then record both observations. The commands themselves do not select a target:

```lua
print(cheatengine_client_coexistence_a_target())
print(cheatengine_client_coexistence_b_target())
print(cheatengine_client_coexistence_a_retain_owner())
print(cheatengine_client_coexistence_a_owner_state())
```

If retain returns `Kind=CapabilityUnavailable`, record that the current tuple cannot perform the owner scenario and
stop this extension; it is not a failed live run and it is not a pass. If it returns `Owner=Retained`, switch Cheat
Engine to target B through the controlled host UI, then refresh **both** plugins and verify A's old lease is released
before any further operation:

```lua
print(cheatengine_client_coexistence_b_target())
print(cheatengine_client_coexistence_a_target())
assert(string.find(cheatengine_client_coexistence_a_owner_state(), "Released=true", 1, true))
print(cheatengine_client_coexistence_a_release_owner())
```

Record both target PIDs, architectures, selection epochs, owner creation/release status, any cleanup failure, and
disable/re-enable results. An unqualified owner, a missing target observation, or an old owner that can act after the
switch is a stopped/failing result, never a reason to continue against target B.

No Cheat Engine execution is performed by this repository fixture or by an ordinary CI build. A managed Native AOT
probe is publication evidence only; it does not prove that Cheat Engine can load, disable, remove, or unload a Native
AOT plugin. No script in this repository produces a receipt for this fixture: host receipts come from the SDK
qualification runner and from the Client qualification work that records Client scenarios against it.
