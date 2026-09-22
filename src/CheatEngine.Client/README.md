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
