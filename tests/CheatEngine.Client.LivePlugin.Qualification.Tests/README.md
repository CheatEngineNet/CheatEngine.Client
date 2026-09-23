# CheatEngine.Client.LivePlugin.Qualification.Tests

## Context

Tests of the Client qualification harness
([`CheatEngine.Client.LivePlugin.Qualification`](../CheatEngine.Client.LivePlugin.Qualification/README.md)) and of the
Client qualification scripts ([`eng/qualification`](../../eng/qualification/README.md)).

## Why this project exists

The harness runs only inside Cheat Engine and the scripts only on an operator's machine, so neither is exercised by CI in
its real setting. Their safety rules must still be proven on every change: the gate that refuses a mutating function, the
guard that confines writes to the declared scratch region, the fault switch, the redaction of observations and logs, the
CI refusal and the isolation of the bundle build. The Lua-free harness files (`Harness/`) are compiled into this module;
the x64 plugin project itself is never referenced, because an AnyCPU test module referencing an x64 project is a
processor-architecture mismatch. The script tests start `pwsh`, which `CheatEngine.Client.Repository.Tests` may not do.

## How it helps improve CheatEngine.Client

- `QualificationAuthorizationTests`: the gate refuses without the exact acknowledgement, with an expired, too long-lived
  or malformed manifest, on another host binary or version, for a target that is the host, has exited or has another
  image, and authorizes only the declared target (`AuthorizationIsDeniedWithoutTheAcknowledgement`,
  `AuthorizationIsDeniedWhenTheManifestExpired`, `AuthorizationIsDeniedWhenTheHostHashDiffers`,
  `AuthorizationIsDeniedForAnotherProcessId`, `AuthorizedManifestAllowsOnlyTheDeclaredTarget`).
- `QualificationWriteGuardTests`: a write outside the declared region, for another process or without a declaration is
  refused (`WritesOutsideADeclaredRegionAreRefused`, `WritesAreRefusedWhenTheDeclarationOrTheClientTargetNamesAnotherProcess`,
  `AbsentDeclarationRefusesEveryWrite`).
- `QualificationFaultInjectionTests`: the runner's fault switch selects exactly one stage and is ignored without
  authorization or with another schema (`FaultFileIsIgnoredWhenAuthorizationIsDenied`,
  `FaultFileWithAnUnknownSchemaIsIgnoredAndReported`, `FaultFileSelectsExactlyTheRequestedStage`,
  `AbsentFaultFileMeansNoFault`).
- `QualificationObservationWriterTests`: observations name their schema and never carry a local path, a failure
  message or exception text, and bound address lists (`ObservationJsonHasTheSchemaIdAndNoLocalPath`,
  `ObservationNeverCarriesFailureMessagesOrExceptionText`, `AddressListsAreBoundedToTheFirstAndLastEntries`).
- `CapturingLoggerProviderTests`: the Q46 sink keeps templates, never formatted messages, and counts sensitive data
  (`CapturedEventsKeepTemplatesButNeverFormattedMessages`, `SensitiveHitsCountAddressesAndDeclaredValues`).
- `Scripts/ClientQualificationScriptTests`: the wrapper refuses CI before any side effect and a work root inside a git
  work tree, prints the SDK runner command with `-WhatIf` without starting anything, and its helpers write an isolated
  NuGet configuration, read package identities from the `.nuspec`, reject an incomplete or foreign bundle closure and
  hash committed JSON with the repository's LF rule.

## Run

From the repository root (the script tests need PowerShell 7, `pwsh`, on `PATH`; they fail rather than skip without it,
and none of them starts Cheat Engine, touches the registry or reads the Cheat Engine installation):

```powershell
dotnet test --project .\tests\CheatEngine.Client.LivePlugin.Qualification.Tests\CheatEngine.Client.LivePlugin.Qualification.Tests.csproj
```
