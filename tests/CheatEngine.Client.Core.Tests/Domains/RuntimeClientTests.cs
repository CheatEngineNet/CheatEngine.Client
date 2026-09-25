using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Results;
using CheatEngine.Client.Runtime;
using CheatEngine.SDK.Engine.Processes;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Targets;
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
		Assert.Equal(new CheatEngineVersion(7, 7, 0, 10621), snapshot.Version.QualifiedCheatEngineBaseline);
		Assert.True(snapshot.Version.IsOnQualifiedCheatEngineLine);
		Assert.Equal(CheatEngineArchitecture.X64, snapshot.Platform.SystemArchitecture);
		Assert.Equal(CheatEngineArchitecture.X64, snapshot.Platform.TargetArchitecture);
		Assert.Equal(PointerSize.Bit64, snapshot.Platform.TargetBitness);
		Assert.Equal(TargetAbi.Windows, snapshot.Platform.TargetAbi);
		Assert.Equal(8, snapshot.Platform.ConfiguredPointerSizeBytes);
		Assert.Equal(CheatEngineOperatingSystem.Windows, snapshot.Platform.HostOperatingSystem);
		Assert.True(snapshot.Platform.IsCheatEngine64Bit);
		Assert.Equal(TargetBackend.LocalProcess, snapshot.Platform.TargetBackend);
		Assert.False(snapshot.Platform.TargetIsAndroid);
		Assert.False(snapshot.Lua.ExternalStateResetDetected);
		Assert.Equal(ClientCapabilityEvidenceState.Satisfied, ProcessSelectionHost(snapshot).State);
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

		Assert.Equal(CheatEngineArchitecture.Unknown, snapshot.Platform.TargetArchitecture);
		Assert.Equal(PointerSize.Unknown, snapshot.Platform.TargetBitness);
		Assert.Equal(TargetAbi.Unknown, snapshot.Platform.TargetAbi);
		Assert.Null(snapshot.Platform.ConfiguredPointerSizeBytes);
		Assert.Null(snapshot.Platform.ConfiguredPointerSizeDiffersFromBitness);
		Assert.Equal(CheatEngineArchitecture.X64, snapshot.Platform.SystemArchitecture);
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

		Assert.True(snapshot.Capabilities.TryGet(ClientCapabilityId.ProcessSelection,
			out ClientCapabilityAvailability processSelection));
		Assert.Equal(ClientCapabilityEvidenceState.Missing, processSelection.Evidence.Host.State);
		Assert.Equal(ClientCapabilityAvailabilityState.Unavailable, processSelection.State);
		Assert.Contains("Process.Current", processSelection.Evidence.Host.Reason, StringComparison.Ordinal);
		Assert.Equal(CheatEngineArchitecture.Unknown, snapshot.Platform.TargetArchitecture);
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
		Assert.Equal(CheatEngineArchitecture.X64, snapshot.Platform.SystemArchitecture);
		Assert.Equal(CheatEngineArchitecture.Unknown, snapshot.Platform.TargetArchitecture);
		Assert.Equal(PointerSize.Unknown, snapshot.Platform.TargetBitness);
		Assert.Equal(kind == ProcessOperationStatusKind.FileAsProcessTarget
			? TargetBackend.FileAsProcess
			: TargetBackend.Unknown, snapshot.Platform.TargetBackend);
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

		Assert.Equal(CheatEngineArchitecture.Arm64, snapshot.Platform.TargetArchitecture);
		Assert.Equal(PointerSize.Bit64, snapshot.Platform.TargetBitness);
		Assert.Equal(TargetAbi.Unix, snapshot.Platform.TargetAbi);
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
		Assert.False(snapshot.Version.IsOnQualifiedCheatEngineLine);
		Assert.Equal(CheatEngineArchitecture.Unknown, snapshot.Platform.SystemArchitecture);
		// The target is observed on its own through the target observation policy.
		Assert.Equal(CheatEngineArchitecture.X64, snapshot.Platform.TargetArchitecture);
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

		Assert.Equal(CheatEngineArchitecture.Unknown, snapshot.Platform.TargetArchitecture);
		Assert.Equal(PointerSize.Bit64, snapshot.Platform.TargetBitness);
		Assert.Equal(TargetAbi.Unknown, snapshot.Platform.TargetAbi);
		Assert.Equal(4, snapshot.Platform.ConfiguredPointerSizeBytes);
		Assert.True(snapshot.Platform.ConfiguredPointerSizeDiffersFromBitness);
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

		Assert.True(snapshot.Capabilities.TryGet(ClientCapabilityId.ProcessSelection,
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

		Assert.Equal(CheatEngineArchitecture.X64, snapshot.Platform.TargetArchitecture);
		Assert.Equal(PointerSize.Bit64, snapshot.Platform.TargetBitness);
		Assert.Equal(PointerSize.Bit32, snapshot.Platform.ConfiguredPointerSize);
		Assert.Equal(4, snapshot.Platform.ConfiguredPointerSizeBytes);
		Assert.True(snapshot.Platform.ConfiguredPointerSizeDiffersFromBitness);
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
		Assert.Equal(PointerSize.Unknown, snapshot.Platform.ConfiguredPointerSize);
		Assert.Equal(PointerSize.Bit64, snapshot.Platform.TargetBitness);
		Assert.True(snapshot.Platform.ConfiguredPointerSizeDiffersFromBitness);
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

		Assert.Equal(expected, snapshot.Platform.TargetArchitecture);
		Assert.Equal(is64Bit ? PointerSize.Bit64 : PointerSize.Bit32, snapshot.Platform.TargetBitness);
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
	[Trait("Qualification", "Q32")]
	public void SnapshotReportsTheBackendOfEveryTargetItObserves()
	{
		// SDK2-08[O]: a CEServer target keeps the facts Cheat Engine reports about it; a file opened as a process is a
		// snapshot too, with its backend and no target fact.
		FakeRuntimeObservationPort remote = new()
		{
			Target = TargetObservations.Create(processId: 900, backend: TargetBackend.CEServer)
		};
		FakeRuntimeObservationPort file = new()
		{
			TargetStatus = ProcessOperationStatus.FileAsProcessTarget
		};

		CheatEngineRuntimeSnapshot remoteSnapshot = new RuntimeClient(new InlineDispatcher(), remote, static () => 1)
			.GetSnapshot(TestContext.Current.CancellationToken);
		CheatEngineRuntimeSnapshot fileSnapshot = new RuntimeClient(new InlineDispatcher(), file, static () => 1)
			.GetSnapshot(TestContext.Current.CancellationToken);

		Assert.Equal(TargetBackend.CEServer, remoteSnapshot.Platform.TargetBackend);
		Assert.Equal(CheatEngineArchitecture.X64, remoteSnapshot.Platform.TargetArchitecture);
		Assert.Equal(TargetBackend.FileAsProcess, fileSnapshot.Platform.TargetBackend);
		Assert.Equal(PointerSize.Unknown, fileSnapshot.Platform.TargetBitness);
		Assert.Equal(CheatEngineOperatingSystem.Windows, fileSnapshot.Platform.HostOperatingSystem);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void SnapshotReportsTheExternalLuaStateResetFact(bool detected)
	{
		// A8: the SDK's sticky reset fact enters the snapshot.
		RuntimeClient runtime = new(new InlineDispatcher(), new FakeRuntimeObservationPort
		{
			ExternalStateResetDetected = detected
		}, static () => 1);

		CheatEngineRuntimeSnapshot snapshot = runtime.GetSnapshot(TestContext.Current.CancellationToken);

		Assert.Equal(detected, snapshot.Lua.ExternalStateResetDetected);
	}

	[Theory]
	[InlineData(SdkVersion + "+" + SdkCommit, true)]
	[InlineData("2.0.1", false)]
	[InlineData(null, false)]
	public void SnapshotReportsTheLoadedSdkPackageAndWhetherItIsTheReviewedOne(string? loaded, bool reviewed)
	{
		ConsumedSdkIdentity identity = new(SdkVersion, SdkCommit, SdkContentHash, SdkSupportedMajor, loaded);
		RuntimeClient runtime = new(new InlineDispatcher(), new FakeRuntimeObservationPort(), static () => 1,
			sdkIdentity: identity);

		CheatEngineRuntimeSnapshot snapshot = runtime.GetSnapshot(TestContext.Current.CancellationToken);

		Assert.Equal(loaded, snapshot.Version.SdkPackageVersion);
		Assert.Equal(reviewed, snapshot.Version.IsReviewedSdkPackage);
	}

	[Fact]
	public void SnapshotComparesTheCheatEngineVersionLineAsIntegers()
	{
		// 7.10 is its own line: the coarse decimal of getCEVersion would have read it as 7.1.
		FakeRuntimeObservationPort port = new()
		{
			Host = FakeRuntimeObservationPort.DefaultHost with
			{
				FileVersion = new CheatEngineVersion(7, 10, 0, 1)
			}
		};
		RuntimeClient runtime = new(new InlineDispatcher(), port, static () => 1);

		CheatEngineRuntimeSnapshot snapshot = runtime.GetSnapshot(TestContext.Current.CancellationToken);

		Assert.Equal(new CheatEngineVersion(7, 10, 0, 1), snapshot.Version.CheatEngineVersion);
		Assert.False(snapshot.Version.IsOnQualifiedCheatEngineLine);
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

		Assert.True(snapshot.Capabilities.TryGet(ClientCapabilityId.ValueScanning,
			out ClientCapabilityAvailability valueScanning));
		// An experimental operational adapter: implemented, but its qualification gate stays unknown (Q25, Q26).
		Assert.Equal(ClientCapabilityAvailabilityState.Unknown, valueScanning.State);
		Assert.Equal(ClientCapabilityEvidenceState.Satisfied, valueScanning.Evidence.Implementation.State);
		Assert.Equal(RuntimeClient.ExperimentalImplementationReason("CECLIENT5001"),
			valueScanning.Evidence.Implementation.Reason);
		// Without an embedded identity the package gate is the evidence's Unknown, as for every other capability.
		Assert.Equal(ClientCapabilityEvidenceState.Unknown, valueScanning.Evidence.Package.State);
		Assert.Equal(ClientCapabilityEvidenceState.Unknown, valueScanning.Evidence.LiveQualification.State);
		// The allocations are experimental too: implemented, with a qualification gate unknown until Q30.a.
		Assert.True(snapshot.Capabilities.TryGet(ClientCapabilityId.Allocations,
			out ClientCapabilityAvailability allocations));
		Assert.Equal(ClientCapabilityAvailabilityState.Unknown, allocations.State);
		Assert.Equal(ClientCapabilityEvidenceState.Satisfied, allocations.Evidence.Implementation.State);
		Assert.Equal(RuntimeClient.ExperimentalImplementationReason("CECLIENT5002"),
			allocations.Evidence.Implementation.Reason);
		Assert.Equal(ClientCapabilityEvidenceState.Unknown, allocations.Evidence.LiveQualification.State);
		Assert.True(snapshot.Capabilities.TryGet(ClientCapabilityId.TypedMemory,
			out ClientCapabilityAvailability typedMemory));
		Assert.Equal(ClientCapabilityAvailabilityState.Unknown, typedMemory.State);
		Assert.True(snapshot.Capabilities.TryGet(ClientCapabilityId.UnsafeLuaExecution,
			out ClientCapabilityAvailability unsafeLua));
		Assert.Equal(ClientCapabilityAvailabilityState.Unavailable, unsafeLua.State);

		// Instruction assembly is experimental too: implemented, with a qualification gate unknown until Q32.
		Assert.True(snapshot.Capabilities.TryGet(ClientCapabilityId.Assembly,
			out ClientCapabilityAvailability assembly));
		Assert.Equal(ClientCapabilityAvailabilityState.Unknown, assembly.State);
		Assert.Equal(ClientCapabilityEvidenceState.Satisfied, assembly.Evidence.Implementation.State);
		Assert.Equal(RuntimeClient.ExperimentalImplementationReason("CECLIENT5003"),
			assembly.Evidence.Implementation.Reason);
		Assert.Equal(ClientCapabilityEvidenceState.Unknown, assembly.Evidence.LiveQualification.State);
		// Every capability composes an operational adapter: every implementation gate is satisfied, and none is
		// available yet.
		Assert.All(snapshot.Capabilities.Entries.ToArray(), static availability =>
		{
			Assert.Equal(ClientCapabilityEvidenceState.Satisfied, availability.Evidence.Implementation.State);
			Assert.False(availability.IsAvailable);
		});
	}

	/// <summary>
	///     The snapshot reports the catalog's capabilities in catalog order, each with a satisfied implementation gate and
	///     the host gate of its row (the catalog is the capability set the other facts of this class use).
	/// </summary>
	[Fact]
	public void SnapshotComposesEveryCapabilityFromItsCatalogRow()
	{
		RuntimeClient runtime = new(new InlineDispatcher(), new FakeRuntimeObservationPort(),
			static () => 1);

		CheatEngineRuntimeSnapshot snapshot = runtime.GetSnapshot(TestContext.Current.CancellationToken);

		Assert.Equal(ClientCapabilityCatalog.Entries.Select(static entry => entry.Id),
			snapshot.Capabilities.Entries.ToArray().Select(static availability => availability.Capability));
		foreach (ClientCapabilityDescriptor entry in ClientCapabilityCatalog.Entries)
		{
			Assert.True(snapshot.Capabilities.TryGet(entry.Id, out ClientCapabilityAvailability availability));
			Assert.Equal(ClientCapabilityEvidenceState.Satisfied, availability.Evidence.Implementation.State);
			Assert.Equal(entry.Host == CapabilityHostSource.SdkSelectedProcess
				? ClientCapabilityEvidenceState.Satisfied
				: ClientCapabilityEvidenceState.Unknown, availability.Evidence.Host.State);
		}

		Assert.Equal(OperationalCapabilities, ClientCapabilityCatalog.Entries.Select(static entry => entry.Id));
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

	[Theory]
	[InlineData(false, ClientCapabilityEvidenceState.Missing, ClientCapabilityAvailabilityState.Unavailable)]
	[InlineData(true, ClientCapabilityEvidenceState.Satisfied, ClientCapabilityAvailabilityState.Unknown)]
	[Trait("Qualification", "Q44")]
	public void SnapshotReportsTheAutoAssemblerPatchesPolicyGateFromTheActivationOptIn(bool enabled,
		ClientCapabilityEvidenceState policy, ClientCapabilityAvailabilityState state)
	{
		RuntimeClient runtime = new(
			new InlineDispatcher(),
			new FakeRuntimeObservationPort(),
			static () => 1,
			policy: new CoreClientPolicy([], false, enableAutoAssemblerPatches: enabled));

		CheatEngineRuntimeSnapshot snapshot = runtime.GetSnapshot(TestContext.Current.CancellationToken);

		Assert.True(snapshot.Capabilities.TryGet(ClientCapabilityId.AutoAssemblerPatches,
			out ClientCapabilityAvailability patches));
		Assert.Equal(policy, patches.Evidence.Policy.State);
		Assert.Equal(ClientCapabilityEvidenceState.Satisfied, patches.Evidence.Implementation.State);
		Assert.Equal(ClientCapabilityEvidenceState.Unknown, patches.Evidence.Host.State);
		Assert.Equal(ClientCapabilityEvidenceState.Unknown, patches.Evidence.LiveQualification.State);
		Assert.Equal(state, patches.State);
		Assert.False(patches.IsAvailable);
		Assert.True(snapshot.Capabilities.TryGet(ClientCapabilityId.UnsafeLuaExecution,
			out ClientCapabilityAvailability unsafeLua));
		Assert.Equal(ClientCapabilityEvidenceState.Missing, unsafeLua.Evidence.Policy.State);
		if (!enabled)
		{
			Assert.Contains("EnableAutoAssemblerPatches", patches.Evidence.Policy.Reason, StringComparison.Ordinal);
		}
	}

	[Fact]
	public void SnapshotReportsAnInactiveActivationAsALifetimeGate()
	{
		RuntimeClient runtime = new(new InlineDispatcher(), new FakeRuntimeObservationPort(), static () => 7,
			isActivationCurrent: static () => false);

		CheatEngineRuntimeSnapshot snapshot = runtime.GetSnapshot(TestContext.Current.CancellationToken);

		Assert.True(snapshot.Capabilities.TryGet(ClientCapabilityId.TypedMemory,
			out ClientCapabilityAvailability availability));
		Assert.Equal(ClientCapabilityEvidenceState.Missing, availability.Evidence.Lifetime.State);
		Assert.Equal(ClientCapabilityAvailabilityState.Unavailable, availability.State);
		Assert.Contains("no longer current", availability.Reason, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void CapabilityQueriesReturnTheObservedSnapshotEntryOrTheDocumentedUnknownFallback()
	{
		RuntimeClient runtime = new(new InlineDispatcher(), new FakeRuntimeObservationPort(), static () => 7);

		bool undefinedSucceeded = runtime.TryGetClientCapability(
			new ClientCapabilityId("Client.Custom"),
			out ClientCapabilityAvailability undefined,
			out CheatEngineFailure undefinedFailure,
			TestContext.Current.CancellationToken);
		bool clientSucceeded = runtime.TryGetClientCapability(
			ClientCapabilityId.ProcessSelection,
			out ClientCapabilityAvailability clientAvailability,
			out CheatEngineFailure clientFailure,
			TestContext.Current.CancellationToken);

		Assert.True(undefinedSucceeded);
		Assert.Equal(default, undefinedFailure);
		Assert.Equal(ClientCapabilityAvailabilityState.Unknown, undefined.State);
		Assert.Contains("does not define a probe or policy gate", undefined.Reason, StringComparison.Ordinal);
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
		Assert.Equal(declaredCapabilities, snapshot.Capabilities.Count);
		foreach (ClientCapabilityAvailability capability in snapshot.Capabilities.Entries)
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

		foreach (ClientCapabilityAvailability capability in snapshot.Capabilities.Entries)
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
		// setting. Every member returns the SDK's status or copied observation and fills only out parameters, except the
		// incarnation that ValidateSelection compares.
		string[] allowed =
		[
			nameof(IRuntimeObservationPort.TryObserveRuntimeInfo), nameof(IRuntimeObservationPort.ObserveHost),
			nameof(IRuntimeObservationPort.TryGetCheatEngineFileVersion),
			nameof(IRuntimeObservationPort.TryGetSystemArchitecture), nameof(IRuntimeObservationPort.TryIsCheatEngine64Bit),
			nameof(IRuntimeObservationPort.TryGetOperatingSystem), nameof(IRuntimeObservationPort.ObserveSelection),
			nameof(IRuntimeObservationPort.ValidateSelection), nameof(ITargetObservationPort.ObserveCurrent),
			nameof(ITargetObservationPort.ObserveTargetArchitecture),
			nameof(ITargetObservationPort.TryGetConfiguredPointerSize)
		];
		System.Reflection.MethodInfo[] members =
		[
			.. typeof(IRuntimeObservationPort).GetMethods().Where(static method => !method.IsSpecialName),
			.. typeof(IRuntimeObservationPort).GetInterfaces().SelectMany(static inherited => inherited.GetMethods())
		];

		Assert.Equal(allowed.Order(StringComparer.Ordinal),
			members.Select(static member => member.Name).Order(StringComparer.Ordinal));
		Assert.All(members, static member =>
		{
			Assert.Contains(member.ReturnType,
				(Type[])
				[
					typeof(ProcessOperationStatus), typeof(LuaOperationStatus), typeof(TargetSelectionFacts),
					typeof(TargetIdentityFacts)
				]);
			Assert.All(member.GetParameters(), static parameter =>
				Assert.True(parameter.IsOut || parameter.ParameterType == typeof(TargetProcessIncarnation),
					parameter.Name));
		});
		Assert.Equal(nameof(IRuntimeObservationPort.ExternalStateResetDetected),
			Assert.Single(typeof(IRuntimeObservationPort).GetProperties()).Name);
		Assert.Empty(typeof(IRuntimeObservationPort).GetEvents());
	}

	private static ClientCapabilityId[] OperationalCapabilities =>
	[
		ClientCapabilityId.ProcessSelection, ClientCapabilityId.TypedMemory, ClientCapabilityId.PatternScanning,
		ClientCapabilityId.ValueScanning, ClientCapabilityId.Inspection, ClientCapabilityId.Tables,
		ClientCapabilityId.ProtectedLua, ClientCapabilityId.UnsafeLuaExecution, ClientCapabilityId.Allocations,
		ClientCapabilityId.Assembly, ClientCapabilityId.AutoAssemblerPatches
	];

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
		Assert.True(snapshot.Capabilities.TryGet(capability, out ClientCapabilityAvailability availability));
		return availability;
	}

	private static ClientCapabilityEvidenceGate ProcessSelectionHost(CheatEngineRuntimeSnapshot snapshot)
	{
		Assert.True(snapshot.Capabilities.TryGet(ClientCapabilityId.ProcessSelection,
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
