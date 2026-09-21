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
generator and bridge assets. `CoexistenceSdkPackageVersion` can be overridden per fixture project only when an operator
has an exact candidate SDK package source and tuple to qualify. A non-default version deliberately disables this
fixture's lock-file write path; it is not an invitation to invent or float package versions.

The source fixture is not a replacement for a clean Client package consumer: `eng/Invoke-PackageSmoke.ps1` remains the
Client package-consumer gate. The coexistence runner imports the Hosting deployment target solely to stage a **fresh**
complete source-fixture closure. It skips the direct-package profile validation because the Client graph is a project
reference. Its receipt labels that distinction explicitly.

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

Run the opt-in preparation runner from the repository root. It builds each project directly into a new bundle directory,
validates the `.deps.json` runtime/native closure plus the plugin, Client, SDK and native bridge assets, hashes every
file, and emits a JSON receipt. It does not start, inspect, attach to, configure, or modify Cheat Engine or a target
process.

```powershell
pwsh .\eng\Invoke-LivePluginCoexistenceFixture.ps1 -Build
```

The output contains three disjoint directories and `coexistence-build-receipt.json`. Preserve all three directories
and the receipt. To prepare a future exact SDK package tuple, pass its version independently for each plugin, point
NuGet at the approved package source through the normal restore configuration, and retain the resulting receipt:

```powershell
pwsh .\eng\Invoke-LivePluginCoexistenceFixture.ps1 -Build `
  -PluginASdkVersion <qualified-A-version> `
  -PluginBSdkVersion <qualified-B-version> `
  -PluginCollisionSdkVersion <qualified-A-version>
```

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

Disable the failed contender if the host exposes it as enabled, then disable B and finally A, recording every lifecycle
result. Stop and retain the failure evidence if a positive plugin cannot load or enable, an expected collision does not
fail, a disabled plugin global remains, or a surviving plugin stops answering. Do not repair a failed observation by
assigning Lua globals manually.

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

No Cheat Engine execution is performed by this repository fixture, runner, or ordinary CI build. A managed Native AOT
probe is publication evidence only; it does not prove that Cheat Engine can load, disable, remove, or unload a Native
AOT plugin. The runner's JSON record is a reproducible build/package-layout receipt, not a `LiveQualified` result.
