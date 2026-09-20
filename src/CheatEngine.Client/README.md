# CheatEngine.Client

The recommended NuGet installation point for high-level, DI-oriented Cheat Engine plugins written in C# 14 and .NET 10.

## Context

`CheatEngine.Client` is a NuGet façade package. It has no operational implementation of its own; it composes the Fluent API and plugin Hosting packages, which in turn bring the public Client contracts and their implementation dependencies.

The public surface is organized by function rather than by delivery assembly. Plugin code uses namespaces such as `CheatEngine.Client`, `CheatEngine.Client.Memory`, `CheatEngine.Client.Scanning`, `CheatEngine.Client.Tables`, `CheatEngine.Client.Lua`, and `CheatEngine.Client.Hosting`. It does not need to use implementation namespaces.

## Why this project exists

Most plugin authors should install one Client package, not reconstruct its package graph. This façade provides that stable installation point while keeping the lower-level packages separately consumable when a project needs only a focused capability.

It deliberately does not hide `CheatEngine.SDK`: a real plugin must directly reference the SDK so that the SDK's source generators, build targets, and native bridge assets are active in the plugin project.

## How it helps CheatEngine.Client

Use this package together with an explicit SDK package reference:

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <LangVersion>14.0</LangVersion>
  <PlatformTarget>x64</PlatformTarget>
  <CheatEngineClientPluginProject>true</CheatEngineClientPluginProject>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="CheatEngine.Client" Version="0.1.0" />
  <PackageReference Include="CheatEngine.SDK" Version="1.0.0" />
</ItemGroup>
```

The direct SDK reference is required even though Hosting has an SDK dependency. When `CheatEngineClientPluginProject` is `true`, Hosting's transitive build target reports `CECLIENT001` if the direct reference is missing.

For a plugin, derive from `CheatEngineClientPlugin`, configure services and sources explicitly, and use `ICheatEngineClient` only within an enabled lifecycle. The Client facade exposes bounded synchronous APIs for runtime capabilities, process selection, typed memory, AOB scanning, inspection, tables, and typed Lua operations. Capability-dependent operations report Client failures when unavailable; value-scan functionality remains capability-gated.

```csharp
using CheatEngine.Client;
using CheatEngine.Client.Hosting;

public sealed class Plugin : CheatEngineClientPlugin
{
    protected override void Configure(CheatEnginePluginBuilder builder)
    {
        // Add explicit configuration, modules, and memory codecs here.
    }

    protected override void OnClientEnabled(ICheatEngineClient client)
    {
        // The Client is valid only for this activation epoch.
    }
}
```

For the complete, SDK-annotated entry point and project configuration, install `CheatEngine.Client.Templates` and create the `ceplugin` template. The template is the executable reference for the expected plugin shape.
