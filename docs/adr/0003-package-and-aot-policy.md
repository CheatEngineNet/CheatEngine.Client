# ADR 0003: Package and AOT policy

- Status: Accepted
- Date: 2026-09-20

## Context

The SDK's plugin entry-point generator and native Lua bridge are activated by a direct package reference in the plugin
project. Indirect NuGet dependencies do not provide a safe substitute for those build assets. At the same time, Native
AOT compatibility analysis can validate library dependencies without proving that Cheat Engine can load a Native AOT
plugin DLL.

## Decision and why

The Client baseline is version `0.1.0`, targets .NET 10 with C# 14, and enables trimming and AOT compatibility analysis
for shipping projects. Central package management pins `CheatEngine.SDK` to `1.0.0` in this repository; SDK-facing
published packages declare the compatible dependency range `[1.0.0, 2.0.0)`.

The standalone template targets Windows x64 and references the Client, SDK, and JSON configuration provider directly:

```xml
<PackageReference Include="CheatEngine.Client" Version="0.1.0" />
<PackageReference Include="CheatEngine.SDK" Version="1.0.0" />
<PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="10.0.12" />
```

The direct `CheatEngine.SDK` reference is deliberate. It activates the generated Cheat Engine plugin entry point and
copies the native Lua bridge. Generated plugin projects set `<CheatEngineClientPluginProject>true</CheatEngineClientPluginProject>`;
the `CheatEngine.Client.Hosting` build target then reports `CECLIENT001` if the project omits that direct SDK reference.

## Delivery verification

Windows CI restores in locked mode; builds and tests Release; packs with public API validation; smoke-tests isolated
package consumption and local template installation; and publishes then runs the `win-x64` Native AOT reference probe.
Live Cheat Engine checks are intentionally outside ordinary CI and remain explicit host-validation gates.

## Consequences and project value

- The maintained template is the source example for real package consumption, including the two direct references a
  plugin needs outside this repository.
- A standalone generated plugin uses explicit local package versions. A future template using central package management
  must bring its own `Directory.Packages.props`; it cannot inherit this repository's file.
- `CheatEngine.Client.AotProbe` validates the shipping graph under Native AOT analysis. It does not claim that Cheat
  Engine can load a Native AOT plugin; supported deployment remains the managed SDK plugin output together with its
  runtime configuration and native bridge.
- A dependency that requires reflection, runtime type discovery, dynamic code, or reflection-based JSON serialization
  needs a trimming-safe alternative before entering a shipping Client project.
