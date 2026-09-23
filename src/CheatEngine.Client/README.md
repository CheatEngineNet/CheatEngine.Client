# CheatEngine.Client

## Context

`CheatEngine.Client` is the umbrella package for a normal in-process Cheat Engine plugin. It is the composition root
of the Client delivery graph: it combines Hosting and Fluent APIs over the public Client contracts, while keeping the
SDK-facing Core implementation behind the DI registration boundary. The package itself intentionally contains no
Cheat Engine host logic.

CheatEngine.Client runs in process inside an enabled Cheat Engine plugin; it is neither Cheat Engine's `luaclient`
library nor an RPC client of `ceserver`.

## Why this project exists

Most plugin projects should take one Client package rather than recreate the Client package graph. This façade is that
stable installation point: Hosting supplies the activation-scoped DI lifecycle, Fluent supplies immutable request
builders, and Core is composed internally through Hosting's DI registration.

It deliberately does not replace `CheatEngine.SDK`. A plugin must reference the SDK **directly** so its build assets
can generate the Cheat Engine entry point and copy the native Lua bridge. Those host-bound assets do not flow through
an ordinary transitive NuGet dependency.

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <LangVersion>14.0</LangVersion>
  <PlatformTarget>x64</PlatformTarget>
  <CheatEngineClientPluginProject>true</CheatEngineClientPluginProject>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="CheatEngine.Client" Version="X.Y.Z" />
  <PackageReference Include="CheatEngine.SDK" Version="1.0.0" />
</ItemGroup>
```

Replace `X.Y.Z` with the CheatEngine.Client version you install; the `ceplugin` template writes it for you. Keep
`CheatEngine.SDK` on 1.x: this Client release is built and tested against CheatEngine.SDK 1.0.0 and declares
`[1.0.0, 2.0.0)`. Do not upgrade to 2.x until a Client release says so.

When `CheatEngineClientPluginProject` is enabled, the Hosting build target emits `CECLIENT001` if that direct SDK
reference is missing.

## Supported host profile

This Client release consumes CheatEngine.SDK 1.0.0 and names one Cheat Engine host profile, the profile that
CheatEngine.SDK 1.0.0 targets. A profile is what a qualification result can name; it is not itself a qualification
result.

| Item | Value |
|---|---|
| Profile id | `ce-7.7.0.10621-x64-managed-hostfxr` |
| Host executable | `cheatengine-x86_64.exe` 7.7.0.10621, machine AMD64, SHA-256 `9727076da50924e4a097b49a02155e4b34759269c3017ff31375364b8826eb4d`; not the `Cheat Engine.exe` launcher and not the `cheatengine-x86_64-SSE4-AVX2.exe` variant |
| Load profile | `managed-hostfxr`: the plugin is a framework-dependent .NET component started by Cheat Engine's nethost/hostfxr route |
| Runtime configuration | The qualification host's `ce.runtimeconfig.json` (`net10.0`), SHA-256 `68f5d81c0a17cc5bdac40bb3d5d88a624f4d31b414f7195ad847d57b0126ac2b`, is a local modification, not an installer baseline |
| Consumed SDK package | `CheatEngine.SDK` 1.0.0, NuGet content hash (SHA-512, base64) `n7nHqZ8vzo7Vf20jF0fkh/jUtR3yo1TwRGpXE7ERxZeJ4C5S/Nsft4lqOg7zGwfsD5Nh9tTVgdw4PrybJRF0gA==` |
| SDK native bridge | `build/native/cheatengine-sdk-lua-bridge.dll`, SHA-256 `da08c2ba03019da3a8c432ef061d5d6133fd2169ba3a6a8e9ac903353856d994` |
| Client qualification | `NotExecuted` for this Client tuple until Client qualification receipts exist (the CheatEngine.Client repository's `docs/qualification/` pages will record them) |

Never edit an installed Cheat Engine to match this profile: its runtime configuration applies to every managed plugin of
the installation, and CheatEngine.Client never treats such an edit as a setup step. A stock installation is not a
qualified profile, and a result on this profile authorizes no x86 or ARM64 plugin claim.

## How it helps improve CheatEngine.Client

The package gives plugin authors a small, intentional composition boundary without exposing implementation or SDK
ownership types. It brings together:

- `CheatEngine.Client.Hosting` for the enable-epoch DI container and plugin lifecycle;
- `CheatEngine.Client.Fluent` for immutable memory and AOB request builders;
- `CheatEngine.Client.Core`, composed through Hosting, as the only SDK mapper;
- functional public namespaces such as `CheatEngine.Client.Memory`, `.Scanning`, `.Tables`, and `.Lua`.

Use the generated `ceplugin` template for a complete, buildable plugin shape. The client and all Client-created
resources are valid only for one enable epoch; do not retain them across disable/re-enable. See the
[repository README](https://github.com/CheatEngineNet/CheatEngine.Client/blob/main/README.md) for installation and
deployment guidance, its
[package architecture](https://github.com/CheatEngineNet/CheatEngine.Client/blob/main/README.md#packages-and-direct-sdk-reference)
section, and its
[plugin lifecycle](https://github.com/CheatEngineNet/CheatEngine.Client/blob/main/README.md#the-plugin-lifecycle)
rules.

## Rules

- This is the only public package where Hosting, Core, and Fluent meet.
- The assembly and root namespace are both `CheatEngine.Client`; do not declare a `CheatEngine` or `Client` type in
  this namespace because CA1724 matches each namespace segment.
