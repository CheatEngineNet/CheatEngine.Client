# CheatEngine.Client.Core.Tests

## Context

This is the unit-test suite for Core's internal adapters and activation-bound orchestration. It uses internal ports and
fakes where a real Cheat Engine host would otherwise be required.

## Why this project exists

Core owns the behavior that is easy to get wrong at the SDK boundary: dispatch admission, epoch expiration, target
selection changes, LIFO resource cleanup, failure mapping, protected Lua calls and module leases, memory codecs, AOB
result ownership, process/runtime adapters, table mutations, and symbol-registration cleanup.

It also verifies the conservative value-scan gate and its managed state machine. It does not fabricate SDK-owned scan
handles or present an unvalidated Cheat Engine 7.7 x64 value-scan lifecycle as supported.

## How it helps improve CheatEngine.Client

The suite separates deterministic policy tests from live-host validation. That lets Core prove cleanup ordering,
rollback behavior, bounded materialization, and stale-resource rejection without mocking SDK statics or requiring a
user process. Regressions in lifecycle code fail quickly before they can leak into a plugin activation.

## Run

From the repository root:

```powershell
dotnet test --project .\tests\CheatEngine.Client.Core.Tests\CheatEngine.Client.Core.Tests.csproj --configuration Release
```

Run the opt-in Cheat Engine live suite separately when it is available and a controlled local fixture has been
authorized; this project is not a substitute for that host-level gate.
