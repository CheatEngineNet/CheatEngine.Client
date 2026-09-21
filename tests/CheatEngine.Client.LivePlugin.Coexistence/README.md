# CheatEngine.Client.LivePlugin.Coexistence

This is an opt-in manual fixture for the Client-facing part of multi-plugin observation. It builds two distinct managed
plugin assemblies, `PluginA` and `PluginB`, with the public `CheatEngineClientPlugin` composition path, generated Lua
modules, and separate output directories. It aligns with the evidence protocol introduced by
[SDK PR #56](https://github.com/CheatEngineNet/CheatEngine.SDK/pull/56): record the identities the exact Cheat Engine
host actually chose; do not manufacture an `AssemblyLoadContext`, infer loader isolation from separate Client DI
providers, or treat a successful build as a live result.

## What the fixture observes

Every plugin enable creates a fresh Client provider and one activation scope. Each fixture module records the SDK
plugin ID, its local Client epoch, and options count, and its two generated Lua globals return a copied
assembly/load-context identity or a monotonic ping count. The identity string includes the plugin, Client Hosting, and
SDK Hosting assemblies plus their module-version IDs and load-context facts. The fixture does not select a process,
mutate memory, allocate or release a target-owned resource, install a hook, create a loader policy, or add a
process-wide synchronization mechanism.

The two plugins deliberately use different Lua globals. That makes this fixture suitable for observing narrow
enable/disable coexistence, but it does **not** qualify overlapping-export collision handling, shared Lua/CE state,
worker concurrency, target switching, retained-owner safety, or side-by-side SDK versions. Those remain the
`Specified_Not_Executed` R25/T049–T050, R26/T051–T052, and R34/T067–T068/T076 scenarios.

## Package and host boundary

The fixture references the changed Client Hosting/source-generator graph so a source build exercises the new Client
contract. It still references the released `CheatEngine.SDK` 1.0.0 package directly, which supplies the SDK entry-point
generator and bridge assets. This source fixture is not a replacement for a clean Client package consumer: package smoke
must validate the eventual Client package. The resolved SDK package is not evidence that it contains the later SDK PR
#56 source merge, nor is that merge a published-package or live-host qualification. Before a real run, identify the
exact qualified Client/SDK package tuple, record package and DLL SHA-256 hashes, and keep the complete dependency
closure for each plugin in its own directory. Never copy DLLs from one output into the other or infer the selected SDK
version from a filename.

Use the controlled Windows x64 Cheat Engine 7.7 profile. Before loading either plugin, record the Cheat Engine binary
version/architecture/SHA-256, .NET and `hostfxr` policy, source or package identities, complete output paths and
hashes, timestamp/operator, and the full diagnostic/Lua transcript. A missing field means an unqualified manual
observation, not a portable hosting claim.

## Build and manual protocol

Build the projects independently from the repository root:

```powershell
dotnet build tests/CheatEngine.Client.LivePlugin.Coexistence/PluginA/CheatEngine.Client.LivePlugin.Coexistence.PluginA.csproj -c Release
dotnet build tests/CheatEngine.Client.LivePlugin.Coexistence/PluginB/CheatEngine.Client.LivePlugin.Coexistence.PluginB.csproj -c Release
```

Keep the two output directories intact. In the controlled host's **Edit > Settings > Plugins** UI, add both plugin DLLs
without changing the installed host configuration. Enable A, then B, and record the result of each command in the Lua
Engine:

```lua
print(cheatengine_client_coexistence_a_identity())
print(cheatengine_client_coexistence_b_identity())
print(cheatengine_client_coexistence_a_ping())
print(cheatengine_client_coexistence_b_ping())
```

Disable A and record that its two globals are absent while B's identity and ping remain callable. Re-enable A, then
disable B and finally A, recording every enable/disable outcome. Stop and retain the failure evidence if either plugin
cannot load or enable, a disabled plugin's global remains, or the surviving plugin stops answering. Do not repair a
failed observation by assigning Lua globals manually.

No Cheat Engine execution is performed by this repository fixture or its ordinary CI build. A managed Native AOT probe
is publication evidence only; it does not prove that Cheat Engine can load, disable, remove, or unload a Native AOT
plugin.
