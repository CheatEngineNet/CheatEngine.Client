# CheatEngine.Client.LivePlugin.Qualification.Tests

## Context

Tests of the Client qualification harness
([`CheatEngine.Client.LivePlugin.Qualification`](../CheatEngine.Client.LivePlugin.Qualification/README.md)).

## Why this project exists

The harness runs only inside Cheat Engine, so it is not exercised by CI in its real setting. Its safety rules must
still be proven on every change: the gate that refuses a mutating function, the guard that confines writes to the
declared scratch region, the fault switch, and the redaction of observations and logs. The Lua-free harness files
(`Harness/`) are compiled into this module; the x64 plugin project itself is never referenced, because an AnyCPU test
module referencing an x64 project is a processor-architecture mismatch.

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

## Run

From the repository root (none of these tests starts Cheat Engine, touches the registry or reads the Cheat Engine
installation):

```powershell
dotnet test --project .\tests\CheatEngine.Client.LivePlugin.Qualification.Tests\CheatEngine.Client.LivePlugin.Qualification.Tests.csproj
```
