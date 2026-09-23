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
the SDK. Value scans remain capability-gated until their full Cheat Engine 7.7 x64 lifecycle has passed the opt-in
live gate.

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
and `CheatEngine.SDK` 1.0.0; keep `CheatEngine.SDK` on 1.x until a Client release says otherwise.

## Validate the template from this repository

Build the repository, pack it, and point the C# consumer smoke tests at the exact package directory:

```powershell
dotnet build CheatEngine.Client.slnx --configuration Release
dotnet pack CheatEngine.Client.slnx --configuration Release --no-build --output ./artifacts/nuget
$env:CHEATENGINE_CLIENT_PACKAGE_SOURCE = (Resolve-Path ./artifacts/nuget).Path
dotnet test --project ./tests/CheatEngine.Client.Tests/CheatEngine.Client.Tests.csproj --configuration Release --no-build --fail-skips on
```

The smoke tests install this template package in an isolated template home, run `dotnet new ceplugin --dry-run`,
instantiate it into a temporary directory outside the repository, restore it against the packed Client packages and
nuget.org only, and build it in Release configuration. They check that the generated project references the co-packed
`CheatEngine.Client` version and the pinned `CheatEngine.SDK` directly, and that the build output holds the complete
deployment closure next to the plugin.
