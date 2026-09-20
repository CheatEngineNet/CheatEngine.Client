# CheatEngine.Client.Fluent.Tests

## Context

This project tests the public fluent builders for AOB scanning and typed memory access. The builders are pure managed
values; they do not retain a Cheat Engine object, Lua state, or SDK ownership handle.

## Why this project exists

The fluent layer is the consumer-facing expression of limits and intent. It must preserve the caller's pattern,
module/range/protection/alignment filters, codec choice, and bounded terminal behavior until execution is delegated to
the Client facade.

## How it helps improve CheatEngine.Client

The tests prove builder immutability, correct forwarding, invalid-argument rejection, and the bounded semantics of
`FirstOrNone`, `RequireSingle`, and `Take`. This keeps ergonomic calls such as `client.Patterns.Aob(...).Take(...)`
predictable without making a live scan part of a unit test.

## Run

From the repository root:

```powershell
dotnet test --project .\tests\CheatEngine.Client.Fluent.Tests\CheatEngine.Client.Fluent.Tests.csproj --configuration Release
```
