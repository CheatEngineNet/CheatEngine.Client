# CheatEngine.Client.Templates

## Context

`CheatEngine.Client.Templates` is the content-only NuGet package that provides the `dotnet new ceplugin` template. It
creates a C# 14, .NET 10, x64, managed in-process Cheat Engine plugin whose composition starts from
`CheatEngineClientPlugin`.

## Why this project exists

Cheat Engine plugins need a direct reference to both packages below:

- `CheatEngine.Client` supplies the high-level facade, fluent API, dependency-injection composition, activation
  lifecycle, and module model.
- `CheatEngine.SDK` supplies the plugin entry-point generator and native Lua bridge build assets. Those assets are not
  guaranteed to flow through a transitive dependency.

The template makes that deployment-critical relationship explicit. Its
`<CheatEngineClientPluginProject>true</CheatEngineClientPluginProject>` marker activates Hosting's full plugin profile:
both references remain direct, exactly one Client plugin is required, and the `net10.0`/C# 14/x64-or-AnyCPU host
contract is checked before compilation.

## How it helps improve CheatEngine.Client

The template turns the intended consumption model into buildable source. It exercises activation-scoped DI, generated
SDK plugin bootstrap, explicit configuration, AOB probing with a bounded copy (post-filtered global scan), typed memory
access, Address List inspection, and
an application-owned Lua module. Keeping this path executable prevents package, bootstrap, and documentation drift.

It deliberately does not imply that a Native AOT binary is loadable by Cheat Engine. The project enables AOT
compatibility analysis for the library graph, but a plugin must be deployed as the complete managed output required by
the SDK. Value scans remain experimental (`CECLIENT5001`) until their full Cheat Engine 7.7 x64 lifecycle has passed
the opt-in live gate, so the template does not use them.

## Create a plugin

Install the published template, then instantiate it from the directory that should contain the new project:

```powershell
dotnet new install CheatEngine.Client.Templates
dotnet new ceplugin --name Contoso.CheatEngine.Plugin --output .\Contoso.CheatEngine.Plugin
dotnet restore .\Contoso.CheatEngine.Plugin\Contoso.CheatEngine.Plugin.csproj
dotnet build .\Contoso.CheatEngine.Plugin\Contoso.CheatEngine.Plugin.csproj --configuration Release --no-restore
```

The generated project's
[README](https://github.com/CheatEngineNet/CheatEngine.Client/blob/main/templates/CheatEngine.Client.Templates/content/CheatEngine.Plugin/README.md)
explains the composition, configuration, and managed deployment requirements. Keep both direct package references when
adapting the project. The generated project references the exact `CheatEngine.Client` version of this template package
and `CheatEngine.SDK` 2.0.0; keep `CheatEngine.SDK` on 2.x until a Client release says otherwise. A 3.x SDK fails the
build with `CECLIENT017`, and a version below 2.0.0 fails the restore with `NU1605`.

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
