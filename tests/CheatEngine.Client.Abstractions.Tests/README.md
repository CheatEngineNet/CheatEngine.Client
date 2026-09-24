# CheatEngine.Client.Abstractions.Tests

## Context

This is the contract test project for the immutable public requests, snapshots, failures, and interfaces used by the
functional CheatEngine.Client namespaces.

## Why this project exists

Contracts are consumed by every layer and should remain meaningful without a running Cheat Engine host. This suite
tests AOB pattern normalization, bounded memory requests and pointer chains, process and runtime snapshots, failure
classification, inspection leases, table definitions, Lua contracts, and the experimental value-scan requests,
values, pages and states.

## How it helps improve CheatEngine.Client

The tests make validation rules and default values executable. They prevent an API change from silently broadening a
request, erasing an expected failure category, or exposing a host-specific behavior in a contract that consumers must
be able to use during design time and unit testing.

They do not run Cheat Engine and do not claim live value-scan support; the value scans are tested here only as an
experimental public contract (`CECLIENT5001`).

## Run

From the repository root:

```powershell
dotnet test --project .\tests\CheatEngine.Client.Abstractions.Tests\CheatEngine.Client.Abstractions.Tests.csproj --configuration Release
```
