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
  <PackageReference Include="CheatEngine.SDK" Version="2.0.0" />
</ItemGroup>
```

Replace `X.Y.Z` with the CheatEngine.Client version you install; the `ceplugin` template writes it for you. Keep
`CheatEngine.SDK` on 2.x: this Client release is built and tested against CheatEngine.SDK 2.0.0 and declares
`[2.0.0, 3.0.0)`. A 3.x SDK fails the build with `CECLIENT017`, and a version below 2.0.0 fails the restore with
`NU1605`. Do not upgrade to 3.x until a Client release says so.

When `CheatEngineClientPluginProject` is enabled, the Hosting build target emits `CECLIENT001` if that direct SDK
reference is missing.

## Supported host profile

This Client release consumes CheatEngine.SDK 2.0.0 and names one Cheat Engine host profile, the qualifiable profile
that CheatEngine.SDK 2.0.0 names. A profile is what a qualification result can name; it is not itself a qualification
result.

| Item | Value |
|---|---|
| Profile id | `ce-7.7.0.10621-x64-managed-hostfxr` |
| Host executable | `cheatengine-x86_64.exe` 7.7.0.10621, machine AMD64, SHA-256 `9727076da50924e4a097b49a02155e4b34759269c3017ff31375364b8826eb4d`; not the `Cheat Engine.exe` launcher and not the `cheatengine-x86_64-SSE4-AVX2.exe` variant |
| Load profile | `managed-hostfxr`: the plugin is a framework-dependent .NET component started by Cheat Engine's nethost/hostfxr route |
| Runtime configuration | The qualification host's `ce.runtimeconfig.json` (`net10.0`), SHA-256 `68f5d81c0a17cc5bdac40bb3d5d88a624f4d31b414f7195ad847d57b0126ac2b`, is a local modification, not an installer baseline |
| Consumed SDK package | `CheatEngine.SDK` 2.0.0, source commit `325c47b573f8bd39a247f1d0101f110fa36c1696`, NuGet content hash (SHA-512, base64) `NLEdZYJ9LKW3EFNB4X5snKCQf7ZS86GkCQ+El7o+S1XQcxHQGjS45Q1ap8lfjQuIwm004mQ3TPxo+ph1yvRrlQ==` |
| SDK native bridge | `build/native/cheatengine-sdk-lua-bridge.dll`, SHA-256 `b008c8d8c136187f241542e6223dc0831999d8300dc2c4c01e1cf49f6fba7698` |
| Client qualification | `NotExecuted` for this Client tuple until Client qualification receipts exist for it (there is no separate qualification documentation tree; receipts, when they exist, are test-owned data under the relevant `*.Repository.Tests` project) |

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

## Versioning and compatibility

CheatEngine.Client follows [Semantic Versioning 2.0.0](https://semver.org/) from 1.0.0. Every Client package is
released with the same version; use one version for all of them.

The seven packages ship in lockstep. Each Client package depends on the Client packages it builds on at exactly its own
version (`[X.Y.Z]` in its nuspec, not the `X.Y.Z` minimum NuGet writes by default), because
`CheatEngine.Client.Extensions.DependencyInjection` and `CheatEngine.Client.Hosting` use internal types of
`CheatEngine.Client.Core` and of `CheatEngine.Client.Extensions.DependencyInjection`, which no public API baseline
protects. Reference `CheatEngine.Client` and let it bring the others; a Client package you reference directly takes the
same version. `CheatEngine.Client.Core` is not a standalone package: it has no public API and is published only as a
dependency of `CheatEngine.Client.Extensions.DependencyInjection`.

- **Patch releases (1.0.x)** fix behavior and documentation without changing the public API.
- **Minor releases (1.x)** add API without breaking code compiled against an earlier 1.x:
  - new types, members, overloads and namespaces;
  - new members on a **call-only** interface. The documentation of every public interface says whether it is
    *Call-only* (the Client implements it and applications call it, for example `ICheatEngineClient`, `IMemoryClient`
    or `ICheatEngineLease`) or *Implementable* (applications implement it and the Client calls it). Implement a
    call-only interface only in a test double, and expect to update the double in a minor release;
  - new enum values. Public enums are `int` enums whose explicit values never change meaning. An outcome enum (a name
    ending in `Kind`, `Status`, `State`, `Effect` or `Scope`) has `Unknown = 0`: handle a value you do not recognize
    like `Unknown`. An option enum has a valid default at 0.
- **Frozen for all of 1.x:** the *Implementable* interfaces (`ILuaModule`, `ILuaOperation<TResult>`,
  `ILuaResultMapper<TSource, TResult>`, `IMemoryCodec<T>` and `ICheatEngineClientModule`) never gain, lose or change a
  member.
- **Experimental APIs**, marked `[Experimental("CECLIENT500x")]`, can change or be removed in a minor release until
  their live qualification passes; using one is an explicit opt-in to that diagnostic.
- **Not contractual:** the text of `CheatEngineFailure.Message` and of exception messages. Classify a failure by
  `CheatEngineFailure.Kind` and `HostEffect`, never by text.
- **CheatEngine.SDK:** Client 1.x depends on CheatEngine.SDK `[2.0.0, 3.0.0)`. A new CheatEngine.SDK major version
  means a new Client major version, never a Client minor release.
- Removing or changing a stable public member, or changing the meaning of a value, happens only in a new major version.

## Rules

- This is the only public package where Hosting, Core, and Fluent meet.
- The assembly and root namespace are both `CheatEngine.Client`; do not declare a `CheatEngine` or `Client` type in
  this namespace because CA1724 matches each namespace segment.
