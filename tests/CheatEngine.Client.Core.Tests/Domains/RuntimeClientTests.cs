using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Results;
using CheatEngine.Client.Runtime;
using CheatEngine.SDK.Engine.Processes;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Tests.Domains;

public sealed class RuntimeClientTests
{
	private const string SdkVersion = "2.0.0";
	private const string SdkCommit = "325c47b573f8bd39a247f1d0101f110fa36c1696";

	private const string SdkContentHash =
		"NLEdZYJ9LKW3EFNB4X5snKCQf7ZS86GkCQ+El7o+S1XQcxHQGjS45Q1ap8lfjQuIwm004mQ3TPxo+ph1yvRrlQ==";

	private const string SdkSupportedMajor = "2";

	// Another CheatEngine.SDK build loaded next to this Client: a prerelease of the next major.
	private const string OtherSdkInformationalVersion = "3.0.0-alpha.0.1+0123456789abcdef0123456789abcdef01234567";

	[Fact]
	public void SnapshotReportsTheFactsOfTheSdkRuntimeSnapshotInOneObservation()
	{
		InlineDispatcher dispatcher = new();
		FakeRuntimeObservationPort port = new();
		RuntimeClient runtime = new(dispatcher, port, static () => 84, new Version(1, 0, 0), new Version(2, 0, 0));

		bool succeeded =
			runtime.TryGetSnapshot(out CheatEngineRuntimeSnapshot snapshot, out CheatEngineFailure failure,
				TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.Equal(84, snapshot.Epoch);
		Assert.Equal(new CheatEngineVersion(7, 7, 0, 10621), snapshot.Version.CheatEngineVersion);
		Assert.Equal(new CheatEngineVersion(7, 7, 0, 10621), snapshot.QualifiedCheatEngineBaseline);
		Assert.True(snapshot.IsOnQualifiedCheatEngineLine);
		Assert.Equal(CheatEngineArchitecture.X64, snapshot.SystemArchitecture);
		Assert.Equal(CheatEngineArchitecture.X64, snapshot.TargetArchitecture);
		Assert.Equal(PointerSize.Bit64, snapshot.TargetPointerSize);
		Assert.Equal(TargetAbi.Windows, snapshot.TargetAbi);
		Assert.Equal(8, snapshot.Platform.ConfiguredPointerSizeBytes);
		Assert.Equal(RuntimeCapabilityAvailabilityState.Available,
			snapshot.SdkCapabilities.GetState(RuntimeCapabilityId.CurrentProcess));
		Assert.Equal(1, dispatcher.InvocationCount);
		// The SDK snapshot answers alone: no host or target fact is read again.
		Assert.Equal([nameof(IRuntimeObservationPort.TryObserveRuntimeInfo)], port.Calls);
		Assert.Empty(port.TargetCalls);
	}

	[Fact]
	[Trait("Qualification", "Q32")]
	public void SnapshotWithoutASelectedTargetLeavesEveryTargetFactUnknown()
	{
		// Spike C3 D2: with no target opened Cheat Engine reports x64-like facts, so the SDK reads none of them.
		FakeRuntimeObservationPort port = new()
		{
			TargetStatus = ProcessOperationStatus.TargetNotAttached
		};
		RuntimeClient runtime = new(new InlineDispatcher(), port, static () => 1);

		CheatEngineRuntimeSnapshot snapshot = runtime.GetSnapshot(TestContext.Current.CancellationToken);

		Assert.Equal(CheatEngineArchitecture.Unknown, snapshot.TargetArchitecture);
		Assert.Equal(PointerSize.Unknown, snapshot.TargetPointerSize);
		Assert.Equal(TargetAbi.Unknown, snapshot.TargetAbi);
		Assert.Null(snapshot.Platform.ConfiguredPointerSizeBytes);
		Assert.Null(snapshot.Platform.ConfiguredPointerSizeDiffersFromTargetPointerSize);
		Assert.Equal(CheatEngineArchitecture.X64, snapshot.SystemArchitecture);
		Assert.Equal(ClientCapabilityEvidenceState.Satisfied, ProcessSelectionHost(snapshot).State);
		Assert.Empty(port.TargetCalls);
	}

	[Fact]
	public void SnapshotReportsAnAbsentTargetGlobalAsAMissingProcessSelectionHostGate()
	{
		FakeRuntimeObservationPort port = new()
		{
			TargetStatus = ProcessOperationStatus.GlobalUnavailable
		};
		RuntimeClient runtime = new(new InlineDispatcher(), port, static () => 1);

		CheatEngineRuntimeSnapshot snapshot = runtime.GetSnapshot(TestContext.Current.CancellationToken);

		Assert.True(snapshot.ClientCapabilities.TryGet(ClientCapabilityId.ProcessSelection,
			out ClientCapabilityAvailability processSelection));
		Assert.Equal(ClientCapabilityEvidenceState.Missing, processSelection.Evidence.Host.State);
		Assert.Equal(ClientCapabilityAvailabilityState.Unavailable, processSelection.State);
		Assert.Contains("Process.Current", processSelection.Evidence.Host.Reason, StringComparison.Ordinal);
		Assert.Equal(CheatEngineArchitecture.Unknown, snapshot.TargetArchitecture);
	}

	[Theory]
	[Trait("Qualification", "Q32")]
	[InlineData(ProcessOperationStatusKind.FileAsProcessTarget)]
	[InlineData(ProcessOperationStatusKind.TargetChanged)]
	public void SnapshotWithoutAnSdkSnapshotStillReportsTheHostFactsAndNoTargetFact(ProcessOperationStatusKind kind)
	{
		// The SDK produces no RuntimeInfo for a file opened as a process or a target change; the host facts are read
		// on their own and no target fact is attributed.
		FakeRuntimeObservationPort port = new()
		{
			TargetStatus = TargetObservations.Status(kind)
		};
		RuntimeClient runtime = new(new InlineDispatcher(), port, static () => 1);

		bool succeeded = runtime.TryGetSnapshot(out CheatEngineRuntimeSnapshot snapshot,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.Equal(new CheatEngineVersion(7, 7, 0, 10621), snapshot.Version.CheatEngineVersion);
		Assert.Equal(CheatEngineArchitecture.X64, snapshot.SystemArchitecture);
		Assert.Equal(CheatEngineArchitecture.Unknown, snapshot.TargetArchitecture);
		Assert.Equal(PointerSize.Unknown, snapshot.TargetPointerSize);
		Assert.Equal(0, snapshot.SdkCapabilities.Count);
		ClientCapabilityEvidenceGate host = ProcessSelectionHost(snapshot);
		Assert.Equal(ClientCapabilityEvidenceState.Unknown, host.State);
		Assert.Contains(kind.ToString(), host.Reason, StringComparison.Ordinal);
		Assert.Equal(
			[nameof(IRuntimeObservationPort.TryObserveRuntimeInfo), nameof(IRuntimeObservationPort.ObserveHost)],
			port.Calls);
	}

	[Fact]
	[Trait("Qualification", "Q32")]
	public void SnapshotOfACeServerTargetKeepsTheFactsCheatEngineReportsAboutIt()
	{
		FakeRuntimeObservationPort port = new()
		{
			Target = TargetObservations.Create(processId: 900, backend: TargetBackend.CEServer, isAndroid: true,
				abiCode: 1, isX86Family: false, isArmFamily: true)
		};
		RuntimeClient runtime = new(new InlineDispatcher(), port, static () => 1);

		CheatEngineRuntimeSnapshot snapshot = runtime.GetSnapshot(TestContext.Current.CancellationToken);

		Assert.Equal(CheatEngineArchitecture.Arm64, snapshot.TargetArchitecture);
		Assert.Equal(PointerSize.Bit64, snapshot.TargetPointerSize);
		Assert.Equal(TargetAbi.Unix, snapshot.TargetAbi);
	}

	[Fact]
	public void SnapshotReadsEachHostFactAloneWhenTheHostObservationFails()
	{
		// One raising global fails the SDK's aggregate host read; the others stay known when read one by one.
		FakeRuntimeObservationPort port = new()
		{
			HostStatus = LuaOperationStatus.LuaFailure(LuaStatus.RuntimeError),
			SystemArchitectureStatus = LuaOperationStatus.InvalidResult,
			FileVersionStatus = LuaOperationStatus.NilResult
		};
		RuntimeClient runtime = new(new InlineDispatcher(), port, static () => 1);

		CheatEngineRuntimeSnapshot snapshot = runtime.GetSnapshot(TestContext.Current.CancellationToken);

		Assert.Null(snapshot.Version.CheatEngineVersion);
		Assert.False(snapshot.IsOnQualifiedCheatEngineLine);
		Assert.Equal(CheatEngineArchitecture.Unknown, snapshot.SystemArchitecture);
		// The target is observed on its own through the target observation policy.
		Assert.Equal(CheatEngineArchitecture.X64, snapshot.TargetArchitecture);
		Assert.Equal(ClientCapabilityEvidenceState.Satisfied, ProcessSelectionHost(snapshot).State);
		Assert.Equal(
		[
			nameof(IRuntimeObservationPort.TryObserveRuntimeInfo), nameof(IRuntimeObservationPort.ObserveHost),
			nameof(IRuntimeObservationPort.TryGetCheatEngineFileVersion),
			nameof(IRuntimeObservationPort.TryGetSystemArchitecture),
			nameof(IRuntimeObservationPort.TryIsCheatEngine64Bit), nameof(IRuntimeObservationPort.TryGetOperatingSystem)
		], port.Calls);
	}

	[Fact]
	[Trait("Qualification", "Q32")]
	public void SnapshotKeepsTheNarrowedTargetFactsWhenOneTargetFactRaises()
	{
		FakeRuntimeObservationPort port = new()
		{
			TargetStatus = TargetObservations.LuaFailure,
			Target = TargetObservations.Create(configuredPointerSizeBytes: 4)
		};
		RuntimeClient runtime = new(new InlineDispatcher(), port, static () => 1);

		CheatEngineRuntimeSnapshot snapshot = runtime.GetSnapshot(TestContext.Current.CancellationToken);

		Assert.Equal(CheatEngineArchitecture.Unknown, snapshot.TargetArchitecture);
		Assert.Equal(PointerSize.Bit64, snapshot.TargetPointerSize);
		Assert.Equal(TargetAbi.Unknown, snapshot.TargetAbi);
		Assert.Equal(4, snapshot.Platform.ConfiguredPointerSizeBytes);
		Assert.True(snapshot.Platform.ConfiguredPointerSizeDiffersFromTargetPointerSize);
	}

	[Theory]
	[InlineData(ProcessOperationStatusKind.GlobalUnavailable, ClientCapabilityEvidenceState.Missing,
		ClientCapabilityAvailabilityState.Unavailable)]
	[InlineData(ProcessOperationStatusKind.ProtectedLuaFailure, ClientCapabilityEvidenceState.Faulted,
		ClientCapabilityAvailabilityState.Unknown)]
	[InlineData(ProcessOperationStatusKind.InvalidResult, ClientCapabilityEvidenceState.Malformed,
		ClientCapabilityAvailabilityState.Unknown)]
	public void SnapshotSeparatesMissingFaultedAndMalformedProcessSelectionEvidence(ProcessOperationStatusKind kind,
		ClientCapabilityEvidenceState expectedHost, ClientCapabilityAvailabilityState expectedAvailability)
	{
		// Without an SDK snapshot the host gate comes from the status of the selected-process observation.
		FakeRuntimeObservationPort port = new()
		{
			RuntimeInfoStatus = TargetObservations.LuaFailure,
			TargetStatus = TargetObservations.Status(kind),
			CurrentReads = [(TargetObservations.Status(kind), 0)]
		};
		RuntimeClient runtime = new(new InlineDispatcher(), port, static () => 7);

		CheatEngineRuntimeSnapshot snapshot = runtime.GetSnapshot(TestContext.Current.CancellationToken);

		Assert.True(snapshot.ClientCapabilities.TryGet(ClientCapabilityId.ProcessSelection,
			out ClientCapabilityAvailability availability));
		Assert.Equal(expectedHost, availability.Evidence.Host.State);
		Assert.Equal(expectedAvailability, availability.State);
	}

	[Fact]
	[Trait("Qualification", "Q31")]
	public void SnapshotKeepsAConfiguredPointerSizeOfFourSeparateFromAnX64Target()
	{
		// Spike C3 D3: setPointerSize(4) on an x64 target leaves targetIs64Bit true and readPointer 8 bytes wide.
		FakeRuntimeObservationPort port = new()
		{
			Target = TargetObservations.Create(configuredPointerSizeBytes: 4)
		};
		RuntimeClient runtime = new(new InlineDispatcher(), port, static () => 1);

		CheatEngineRuntimeSnapshot snapshot = runtime.GetSnapshot(TestContext.Current.CancellationToken);

		Assert.Equal(CheatEngineArchitecture.X64, snapshot.TargetArchitecture);
		Assert.Equal(PointerSize.Bit64, snapshot.TargetPointerSize);
		Assert.Equal(PointerSize.Bit32, snapshot.ConfiguredPointerSize);
		Assert.Equal(4, snapshot.Platform.ConfiguredPointerSizeBytes);
		Assert.True(snapshot.Platform.ConfiguredPointerSizeDiffersFromTargetPointerSize);
	}

	[Fact]
	[Trait("Qualification", "Q31")]
	public void SnapshotKeepsARawConfiguredPointerSizeOutsideFourAndEight()
	{
		// Spike C3 D3(b): setPointerSize accepts any integer; 2 was stored and read back.
		FakeRuntimeObservationPort port = new()
		{
			Target = TargetObservations.Create(configuredPointerSizeBytes: 2)
		};
		RuntimeClient runtime = new(new InlineDispatcher(), port, static () => 1);

		CheatEngineRuntimeSnapshot snapshot = runtime.GetSnapshot(TestContext.Current.CancellationToken);

		Assert.Equal(2, snapshot.Platform.ConfiguredPointerSizeBytes);
		Assert.Equal(PointerSize.Unknown, snapshot.ConfiguredPointerSize);
		Assert.Equal(PointerSize.Bit64, snapshot.TargetPointerSize);
		Assert.True(snapshot.Platform.ConfiguredPointerSizeDiffersFromTargetPointerSize);
	}

	[Theory]
	[Trait("Qualification", "Q32")]
	[InlineData(true, false, true, CheatEngineArchitecture.X64)]
	[InlineData(true, false, false, CheatEngineArchitecture.X86)]
	[InlineData(false, true, true, CheatEngineArchitecture.Arm64)]
	[InlineData(false, true, false, CheatEngineArchitecture.Arm32)]
	[InlineData(true, true, true, CheatEngineArchitecture.Unknown)]
	[InlineData(false, false, false, CheatEngineArchitecture.Unknown)]
	[InlineData(null, null, true, CheatEngineArchitecture.Unknown)]
	[InlineData(true, null, false, CheatEngineArchitecture.Unknown)]
	public void SnapshotReportsTheSdkIsaDerivationAndNeverOneFromTheBitnessAlone(bool? isX86, bool? isArm,
		bool is64Bit, CheatEngineArchitecture expected)
	{
		FakeRuntimeObservationPort port = new()
		{
			Target = TargetObservations.Create(is64Bit: is64Bit, isX86Family: isX86, isArmFamily: isArm)
		};
		RuntimeClient runtime = new(new InlineDispatcher(), port, static () => 1);

		CheatEngineRuntimeSnapshot snapshot = runtime.GetSnapshot(TestContext.Current.CancellationToken);

		Assert.Equal(expected, snapshot.TargetArchitecture);
		Assert.Equal(is64Bit ? PointerSize.Bit64 : PointerSize.Bit32, snapshot.TargetPointerSize);
	}

	[Fact]
	public void TryGetSnapshotObservesCancellationBeforeDispatchingAnObservation()
	{
		InlineDispatcher dispatcher = new();
		FakeRuntimeObservationPort port = new();
		RuntimeClient runtime = new(dispatcher, port, static () => 1);
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();

		bool succeeded = runtime.TryGetSnapshot(out CheatEngineRuntimeSnapshot snapshot, out CheatEngineFailure failure,
			cancellation.Token);

		Assert.False(succeeded);
		Assert.Equal(default, snapshot);
		Assert.Equal(CheatEngineFailureKind.Cancelled, failure.Kind);
		Assert.Equal(0, dispatcher.InvocationCount);
		Assert.Empty(port.Calls);
	}

	[Fact]
	public void TryGetSdkCapabilityReturnsUnknownForAnIdentifierThatWasNotProbed()
	{
		RuntimeClient runtime = new(new InlineDispatcher(), new FakeRuntimeObservationPort(), static () => 1);
		RuntimeCapabilityId customCapability = new("Client.Custom");

		bool succeeded = runtime.TryGetSdkCapability(
			customCapability,
			out RuntimeCapabilityAvailability availability,
			out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.Equal(customCapability, availability.Capability);
		Assert.Equal(RuntimeCapabilityAvailabilityState.Unknown, availability.State);
	}

	[Fact]
	public void SnapshotReturnsADetachedRuntimeFaultAsAFailureInsteadOfThrowing()
	{
		// A detached SDK runtime refuses the Lua admission with InvalidOperationException; the SdkBoundary rule keeps it
		// from crossing the Try method (F15).
		InvalidOperationException detached = new("The plugin is not enabled.");
		RuntimeClient runtime = new(new InlineDispatcher(), new FakeRuntimeObservationPort { Fault = detached },
			static () => 1);

		bool succeeded = runtime.TryGetSnapshot(out CheatEngineRuntimeSnapshot snapshot,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, snapshot);
		Assert.Equal("Runtime.GetSnapshot", failure.Operation);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Same(detached, failure.Exception);
	}

	[Fact]
	public void SnapshotReportsClientGatesWithoutClaimingUnprobedDomainsAreAvailable()
	{
		RuntimeClient runtime = new(new InlineDispatcher(), new FakeRuntimeObservationPort(), static () => 1);

		CheatEngineRuntimeSnapshot snapshot = runtime.GetSnapshot(TestContext.Current.CancellationToken);

		Assert.True(snapshot.ClientCapabilities.TryGet(ClientCapabilityId.ValueScanning,
			out ClientCapabilityAvailability valueScanning));
		Assert.Equal(ClientCapabilityAvailabilityState.Unavailable, valueScanning.State);
		Assert.Equal(ClientCapabilityEvidenceState.Missing, valueScanning.Evidence.Implementation.State);
		// Without an embedded identity the package gate is the evidence's Unknown, as for every other capability.
		Assert.Equal(ClientCapabilityEvidenceState.Unknown, valueScanning.Evidence.Package.State);
		Assert.Equal(RuntimeClient.ContractOnlyReason, valueScanning.Reason);
		Assert.True(snapshot.ClientCapabilities.TryGet(ClientCapabilityId.TypedMemory,
			out ClientCapabilityAvailability typedMemory));
		Assert.Equal(ClientCapabilityAvailabilityState.Unknown, typedMemory.State);
		Assert.True(snapshot.ClientCapabilities.TryGet(ClientCapabilityId.UnsafeLuaExecution,
			out ClientCapabilityAvailability unsafeLua));
		Assert.Equal(ClientCapabilityAvailabilityState.Unavailable, unsafeLua.State);

		ClientCapabilityId[] contractOnlyCapabilities =
		[
			ClientCapabilityId.Allocations,
			ClientCapabilityId.Assembly
		];
		foreach (ClientCapabilityId capability in contractOnlyCapabilities)
		{
			Assert.True(snapshot.ClientCapabilities.TryGet(capability, out ClientCapabilityAvailability availability));
			Assert.Equal(ClientCapabilityAvailabilityState.Unavailable, availability.State);
			Assert.False(availability.IsAvailable);
			Assert.Equal(ClientCapabilityEvidenceState.Missing, availability.Evidence.Implementation.State);
			Assert.Equal(ClientCapabilityEvidenceState.Unknown, availability.Evidence.Package.State);
		}
	}

	/// <summary>
	///     The snapshot reports the catalog's capabilities in catalog order, each with the implementation gate of its row
	///     (the operational set is the one the other facts of this class use).
	/// </summary>
	[Fact]
	public void SnapshotComposesEveryCapabilityFromItsCatalogRow()
	{
		RuntimeClient runtime = new(new InlineDispatcher(), new FakeRuntimeObservationPort(),
			static () => 1);

		CheatEngineRuntimeSnapshot snapshot = runtime.GetSnapshot(TestContext.Current.CancellationToken);

		Assert.Equal(ClientCapabilityCatalog.Entries.Select(static entry => entry.Id),
			snapshot.ClientCapabilities.Entries.ToArray().Select(static availability => availability.Capability));
		foreach (ClientCapabilityDescriptor entry in ClientCapabilityCatalog.Entries)
		{
			Assert.True(snapshot.ClientCapabilities.TryGet(entry.Id, out ClientCapabilityAvailability availability));
			Assert.Equal(entry.Implementation == CapabilityImplementation.Operational
				? ClientCapabilityEvidenceState.Satisfied
				: ClientCapabilityEvidenceState.Missing, availability.Evidence.Implementation.State);
			Assert.Equal(entry.Host == CapabilityHostSource.SdkSelectedProcess
				? ClientCapabilityEvidenceState.Satisfied
				: ClientCapabilityEvidenceState.Unknown, availability.Evidence.Host.State);
		}

		Assert.Equal(OperationalCapabilities, ClientCapabilityCatalog.Entries
			.Where(static entry => entry.Implementation == CapabilityImplementation.Operational)
			.Select(static entry => entry.Id));
	}

	[Fact]
	public void SnapshotReportsUnsafeLuaPolicyWithoutTreatingItAsHostEvidence()
	{
		RuntimeClient runtime = new(
			new InlineDispatcher(),
			new FakeRuntimeObservationPort(),
			static () => 1,
			policy: new CoreClientPolicy([], true));

		bool succeeded = runtime.TryGetClientCapability(
			ClientCapabilityId.UnsafeLuaExecution,
			out ClientCapabilityAvailability availability,
			out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.Equal(ClientCapabilityEvidenceState.Satisfied, availability.Evidence.Policy.State);
		Assert.Equal(ClientCapabilityEvidenceState.Unknown, availability.Evidence.Host.State);
		Assert.Equal(ClientCapabilityEvidenceState.Unknown, availability.Evidence.LiveQualification.State);
		Assert.Equal(ClientCapabilityAvailabilityState.Unknown, availability.State);
		Assert.False(availability.IsAvailable);
	}

	[Fact]
	public void SnapshotReportsAnInactiveActivationAsALifetimeGate()
	{
		RuntimeClient runtime = new(new InlineDispatcher(), new FakeRuntimeObservationPort(), static () => 7,
			isActivationCurrent: static () => false);

		CheatEngineRuntimeSnapshot snapshot = runtime.GetSnapshot(TestContext.Current.CancellationToken);

		Assert.True(snapshot.ClientCapabilities.TryGet(ClientCapabilityId.TypedMemory,
			out ClientCapabilityAvailability availability));
		Assert.Equal(ClientCapabilityEvidenceState.Missing, availability.Evidence.Lifetime.State);
		Assert.Equal(ClientCapabilityAvailabilityState.Unavailable, availability.State);
		Assert.Contains("no longer current", availability.Reason, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void CapabilityQueriesReturnTheObservedSnapshotEntryOrTheDocumentedUnknownFallback()
	{
		RuntimeClient runtime = new(new InlineDispatcher(), new FakeRuntimeObservationPort(), static () => 7);

		bool sdkSucceeded = runtime.TryGetSdkCapability(
			RuntimeCapabilityId.CheatEngineVersion,
			out RuntimeCapabilityAvailability sdkAvailability,
			out CheatEngineFailure sdkFailure,
			TestContext.Current.CancellationToken);
		bool clientSucceeded = runtime.TryGetClientCapability(
			ClientCapabilityId.ProcessSelection,
			out ClientCapabilityAvailability clientAvailability,
			out CheatEngineFailure clientFailure,
			TestContext.Current.CancellationToken);

		Assert.True(sdkSucceeded);
		Assert.Equal(default, sdkFailure);
		Assert.Equal(RuntimeCapabilityAvailabilityState.Available, sdkAvailability.State);
		Assert.True(clientSucceeded);
		Assert.Equal(default, clientFailure);
		Assert.Equal(ClientCapabilityAvailabilityState.Unknown, clientAvailability.State);
		Assert.Equal(ClientCapabilityEvidenceState.Unknown, clientAvailability.Evidence.Package.State);
		Assert.Contains("package artifact", clientAvailability.Reason, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void CapabilityQueriesRejectDefaultIdentifiersBeforeDispatching()
	{
		InlineDispatcher dispatcher = new();
		RuntimeClient runtime = new(dispatcher, new FakeRuntimeObservationPort(), static () => 7);

		Assert.Throws<ArgumentException>(() => runtime.TryGetSdkCapability(
			default, out _, out _, TestContext.Current.CancellationToken));
		Assert.Throws<ArgumentException>(() => runtime.TryGetClientCapability(
			default, out _, out _, TestContext.Current.CancellationToken));
		Assert.Equal(0, dispatcher.InvocationCount);
	}

	[Theory]
	[InlineData(SdkVersion + "+" + SdkCommit, ClientCapabilityEvidenceState.Satisfied, true)]
	[InlineData("2.0.1", ClientCapabilityEvidenceState.Satisfied, false)]
	[InlineData("2.1.0-beta.1", ClientCapabilityEvidenceState.Satisfied, false)]
	[InlineData("2.0.0-rc.1", ClientCapabilityEvidenceState.Missing, false)]
	[InlineData("1.0.0", ClientCapabilityEvidenceState.Missing, false)]
	[InlineData("3.0.0", ClientCapabilityEvidenceState.Missing, false)]
	[InlineData(OtherSdkInformationalVersion, ClientCapabilityEvidenceState.Missing, false)]
	[InlineData(null, ClientCapabilityEvidenceState.Unknown, false)]
	public void PackageGateAcceptsEveryReleaseOfTheSupportedMajorAtOrAboveThePin(string? loaded,
		ClientCapabilityEvidenceState expected, bool exact)
	{
		// The gate follows the declared dependency range: same major as the pin and at least the pin by SemVer
		// precedence (a prerelease of the pin is below it); the reason says whether it is the reviewed package itself.
		ConsumedSdkIdentity identity = new(SdkVersion, SdkCommit, SdkContentHash, SdkSupportedMajor, loaded);

		ClientCapabilityAvailability typedMemory = GetClientCapability(identity, ClientCapabilityId.TypedMemory);
		string reason = typedMemory.Evidence.Package.Reason;

		Assert.Equal(expected, typedMemory.Evidence.Package.State);
		Assert.Equal(exact, identity.ExactReviewedIdentity);
		Assert.Equal(expected == ClientCapabilityEvidenceState.Missing
			? ClientCapabilityAvailabilityState.Unavailable
			: ClientCapabilityAvailabilityState.Unknown, typedMemory.State);
		Assert.DoesNotContain("refuse", reason, StringComparison.OrdinalIgnoreCase);
		if (loaded is not null)
		{
			Assert.Contains(loaded, reason, StringComparison.Ordinal);
		}

		if (exact)
		{
			Assert.Contains("exactly the reviewed package", reason, StringComparison.Ordinal);
			Assert.Contains(SdkContentHash, reason, StringComparison.Ordinal);
		}
		else if (expected == ClientCapabilityEvidenceState.Satisfied)
		{
			Assert.Contains("is a release of the supported CheatEngine.SDK 2.x at or above 2.0.0", reason,
				StringComparison.Ordinal);
			Assert.Contains($"another release than the reviewed package this Client build consumed ({SdkVersion}+{SdkCommit})",
				reason, StringComparison.Ordinal);
		}
		else if (expected == ClientCapabilityEvidenceState.Missing)
		{
			Assert.Contains("is not a release of the supported CheatEngine.SDK 2.x at or above 2.0.0", reason,
				StringComparison.Ordinal);
		}
		else
		{
			Assert.Contains("declares no informational version", reason, StringComparison.Ordinal);
		}

		foreach (ClientCapabilityId operational in OperationalCapabilities)
		{
			Assert.Equal(identity.PackageGate, GetClientCapability(identity, operational).Evidence.Package);
		}
	}

	[Fact]
	public void PackageGateIsUnknownWithoutEmbeddedIdentity()
	{
		ConsumedSdkIdentity identity = new(null, null, null, null, $"{SdkVersion}+{SdkCommit}");

		ClientCapabilityAvailability typedMemory = GetClientCapability(identity, ClientCapabilityId.TypedMemory);

		Assert.False(identity.IsEmbedded);
		Assert.Equal(ClientCapabilityEvidenceState.Unknown, typedMemory.Evidence.Package.State);
		Assert.Contains("embeds no consumed CheatEngine.SDK identity", typedMemory.Evidence.Package.Reason,
			StringComparison.Ordinal);
	}

	[Fact]
	public void TheIdentityOfThisBuildMatchesTheLoadedSdkPackage()
	{
		// The Core assembly under test embeds the identity of its locked and restored CheatEngine.SDK package (a
		// build that cannot embed it fails with CHEATENGINECLIENT9050), and the test process loads that package, so
		// the production identity is embedded and Satisfied.
		ConsumedSdkIdentity current = ConsumedSdkIdentity.Current;

		Assert.True(current.IsEmbedded, "The Core assembly under test embeds no consumed CheatEngine.SDK identity.");
		Assert.Equal(ClientCapabilityEvidenceState.Satisfied, current.PackageGate.State);
		Assert.True(current.ExactReviewedIdentity);
		Assert.Equal("the reviewed package", current.IdentityLabel);
		Assert.Equal(typeof(RuntimeInfo).Assembly
				.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
				.Cast<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion,
			current.LoadedInformationalVersion);
	}

	[Theory]
	[Trait("Qualification", "Q44")]
	[InlineData(nameof(ClientCapabilityId.ValueScanning))]
	[InlineData(nameof(ClientCapabilityId.Allocations))]
	[InlineData(nameof(ClientCapabilityId.Assembly))]
	public void ContractOnlyCapabilitiesAreRefusedByTheImplementationGateWithAPackageGateFromEvidence(string name)
	{
		// ADR-10: the package gate states what the evidence shows about the consumed package, never a claim about what
		// an SDK version provides; the contract-only implementation gate alone keeps the capability unavailable.
		ClientCapabilityId capability = ContractOnlyCapability(name);
		ConsumedSdkIdentity differentPackage = new(SdkVersion, SdkCommit, SdkContentHash, SdkSupportedMajor,
			OtherSdkInformationalVersion);

		ClientCapabilityAvailability matching = GetClientCapability(MatchingIdentity(), capability);
		ClientCapabilityAvailability different = GetClientCapability(differentPackage, capability);
		ClientCapabilityAvailability notEmbedded = GetClientCapability(ConsumedSdkIdentity.NotEmbedded, capability);

		ClientCapabilityAvailability[] availabilities = [matching, different, notEmbedded];
		foreach (ClientCapabilityAvailability availability in availabilities)
		{
			Assert.Equal(ClientCapabilityAvailabilityState.Unavailable, availability.State);
			Assert.False(availability.IsAvailable);
			Assert.Equal(ClientCapabilityEvidenceState.Missing, availability.Evidence.Implementation.State);
			Assert.Equal(RuntimeClient.ContractOnlyReason, availability.Evidence.Implementation.Reason);
			Assert.Equal(RuntimeClient.ContractOnlyReason, availability.Reason);
		}

		Assert.Equal(MatchingIdentity().PackageGate, matching.Evidence.Package);
		Assert.Equal(ClientCapabilityEvidenceState.Satisfied, matching.Evidence.Package.State);
		Assert.Equal(differentPackage.PackageGate, different.Evidence.Package);
		Assert.Equal(ClientCapabilityEvidenceState.Missing, different.Evidence.Package.State);
		Assert.Equal(ConsumedSdkIdentity.NotEmbedded.PackageGate, notEmbedded.Evidence.Package);
		Assert.Equal(ClientCapabilityEvidenceState.Unknown, notEmbedded.Evidence.Package.State);
	}

	[Fact]
	[Trait("Qualification", "Q44")]
	public void NoCapabilityIsAvailableWithoutEverySixGates()
	{
		// Even with a matching package, a selected target and an enabled unsafe-Lua policy, the qualification gate stays
		// unknown and the unprobed host gates stay unknown: no capability is announced as available (ADR-09).
		RuntimeClient runtime = new(new InlineDispatcher(), new FakeRuntimeObservationPort(),
			static () => 1, policy: new CoreClientPolicy([], true), sdkIdentity: MatchingIdentity());

		CheatEngineRuntimeSnapshot snapshot = runtime.GetSnapshot(TestContext.Current.CancellationToken);

		int declaredCapabilities = typeof(ClientCapabilityId)
			.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
			.Count(static property => property.PropertyType == typeof(ClientCapabilityId));
		Assert.Equal(declaredCapabilities, snapshot.ClientCapabilities.Count);
		foreach (ClientCapabilityAvailability capability in snapshot.ClientCapabilities.Entries)
		{
			Assert.False(capability.IsAvailable, capability.Capability.Value);
			Assert.NotEqual(ClientCapabilityAvailabilityState.Available, capability.State);
			Assert.False(capability.Evidence.IsExecutable);
		}
	}

	[Fact]
	[Trait("Qualification", "Q44")]
	public void QualificationGateStaysUnknownWithoutACommittedClientReceipt()
	{
		RuntimeClient runtime = new(new InlineDispatcher(), new FakeRuntimeObservationPort(),
			static () => 1, sdkIdentity: MatchingIdentity());

		CheatEngineRuntimeSnapshot snapshot = runtime.GetSnapshot(TestContext.Current.CancellationToken);
		string reason = RuntimeClient.QualificationUnknownReason(MatchingIdentity());

		foreach (ClientCapabilityAvailability capability in snapshot.ClientCapabilities.Entries)
		{
			Assert.Equal(ClientCapabilityEvidenceState.Unknown, capability.Evidence.LiveQualification.State);
			Assert.Equal(reason, capability.Evidence.LiveQualification.Reason);
		}

		Assert.Contains("SDK-branch receipts never qualify the Client tuple", reason, StringComparison.Ordinal);
		Assert.Contains(ConsumedSdkIdentity.SupportedHostProfileId, reason, StringComparison.Ordinal);
		// The Client tuple names the embedded identity, not a version written in the source.
		Assert.Contains($"CheatEngine.SDK {SdkVersion}+{SdkCommit}", reason, StringComparison.Ordinal);
	}

	[Fact]
	[Trait("Qualification", "Q44")]
	public void QualificationReasonNamesNoSdkVersionWithoutAnEmbeddedIdentity()
	{
		string reason = RuntimeClient.QualificationUnknownReason(ConsumedSdkIdentity.NotEmbedded);

		Assert.Contains("embeds no identity", reason, StringComparison.Ordinal);
		Assert.Contains(ConsumedSdkIdentity.SupportedHostProfileId, reason, StringComparison.Ordinal);
		Assert.DoesNotMatch(@"CheatEngine\.SDK \d", reason);
	}

	[Fact]
	[Trait("Qualification", "Q45")]
	public void TheRuntimeObservationPortExposesOnlyObservationMembers()
	{
		// Q45 / ADR-09a: a runtime observation observes; it never selects a process, loads a table or changes a host
		// setting. Every member returns the SDK's status and fills only out parameters.
		string[] allowed =
		[
			nameof(IRuntimeObservationPort.TryObserveRuntimeInfo), nameof(IRuntimeObservationPort.ObserveHost),
			nameof(IRuntimeObservationPort.TryGetCheatEngineFileVersion),
			nameof(IRuntimeObservationPort.TryGetSystemArchitecture), nameof(IRuntimeObservationPort.TryIsCheatEngine64Bit),
			nameof(IRuntimeObservationPort.TryGetOperatingSystem), nameof(ITargetObservationPort.ObserveCurrent),
			nameof(ITargetObservationPort.ObserveTargetArchitecture),
			nameof(ITargetObservationPort.TryGetConfiguredPointerSize)
		];
		System.Reflection.MethodInfo[] members =
		[
			.. typeof(IRuntimeObservationPort).GetMethods(),
			.. typeof(IRuntimeObservationPort).GetInterfaces().SelectMany(static inherited => inherited.GetMethods())
		];

		Assert.Equal(allowed.Order(StringComparer.Ordinal),
			members.Select(static member => member.Name).Order(StringComparer.Ordinal));
		Assert.All(members, static member =>
		{
			Assert.Contains(member.ReturnType, (Type[]) [typeof(ProcessOperationStatus), typeof(LuaOperationStatus)]);
			Assert.All(member.GetParameters(), static parameter => Assert.True(parameter.IsOut, parameter.Name));
		});
		Assert.Empty(typeof(IRuntimeObservationPort).GetProperties());
		Assert.Empty(typeof(IRuntimeObservationPort).GetEvents());
	}

	private static ClientCapabilityId[] OperationalCapabilities =>
	[
		ClientCapabilityId.ProcessSelection, ClientCapabilityId.TypedMemory, ClientCapabilityId.PatternScanning,
		ClientCapabilityId.Inspection, ClientCapabilityId.Tables, ClientCapabilityId.ProtectedLua,
		ClientCapabilityId.UnsafeLuaExecution
	];

	private static ClientCapabilityId ContractOnlyCapability(string name)
	{
		return name switch
		{
			nameof(ClientCapabilityId.ValueScanning) => ClientCapabilityId.ValueScanning,
			nameof(ClientCapabilityId.Allocations) => ClientCapabilityId.Allocations,
			nameof(ClientCapabilityId.Assembly) => ClientCapabilityId.Assembly,
			_ => throw new ArgumentOutOfRangeException(nameof(name), name, null)
		};
	}

	private static ConsumedSdkIdentity MatchingIdentity()
	{
		return new ConsumedSdkIdentity(SdkVersion, SdkCommit, SdkContentHash, SdkSupportedMajor,
			$"{SdkVersion}+{SdkCommit}");
	}

	private static ClientCapabilityAvailability GetClientCapability(ConsumedSdkIdentity identity,
		ClientCapabilityId capability)
	{
		RuntimeClient runtime = new(new InlineDispatcher(), new FakeRuntimeObservationPort(),
			static () => 1, sdkIdentity: identity);
		CheatEngineRuntimeSnapshot snapshot = runtime.GetSnapshot(TestContext.Current.CancellationToken);
		Assert.True(snapshot.ClientCapabilities.TryGet(capability, out ClientCapabilityAvailability availability));
		return availability;
	}

	private static ClientCapabilityEvidenceGate ProcessSelectionHost(CheatEngineRuntimeSnapshot snapshot)
	{
		Assert.True(snapshot.ClientCapabilities.TryGet(ClientCapabilityId.ProcessSelection,
			out ClientCapabilityAvailability availability));
		return availability.Evidence.Host;
	}

	private sealed class InlineDispatcher : ICheatEngineDispatcher
	{
		internal int InvocationCount
		{
			get;
			private set;
		}

		public bool IsMainThread => true;

		public bool TryInvoke(Action callback, out CheatEngineFailure failure,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(callback);
			if (cancellationToken.IsCancellationRequested)
			{
				failure = Cancelled();
				return false;
			}

			InvocationCount++;
			callback();
			failure = default;
			return true;
		}

		public bool TryInvoke<T>(Func<T> callback, out T result, out CheatEngineFailure failure,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(callback);
			if (cancellationToken.IsCancellationRequested)
			{
				result = default!;
				failure = Cancelled();
				return false;
			}

			InvocationCount++;
			result = callback();
			failure = default;
			return true;
		}

		public void Invoke(Action callback, CancellationToken cancellationToken = default)
		{
			if (!TryInvoke(callback, out CheatEngineFailure failure, cancellationToken))
			{
				failure.Throw(cancellationToken);
			}
		}

		public T Invoke<T>(Func<T> callback, CancellationToken cancellationToken = default)
		{
			if (TryInvoke(callback, out T result, out CheatEngineFailure failure, cancellationToken))
			{
				return result;
			}

			failure.Throw(cancellationToken);
			return default!;
		}

		private static CheatEngineFailure Cancelled()
		{
			return new CheatEngineFailure(
				CheatEngineFailureKind.Cancelled,
				"Dispatcher.Invoke",
				"Cancelled before dispatch.");
		}
	}
}
