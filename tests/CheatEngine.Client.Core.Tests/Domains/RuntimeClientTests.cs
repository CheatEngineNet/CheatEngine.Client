using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Results;
using CheatEngine.Client.Runtime;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Tests.Domains;

public sealed class RuntimeClientTests
{
	private const string SdkVersion = "2.0.0";
	private const string SdkCommit = "325c47b573f8bd39a247f1d0101f110fa36c1696";

	private const string SdkContentHash =
		"NLEdZYJ9LKW3EFNB4X5snKCQf7ZS86GkCQ+El7o+S1XQcxHQGjS45Q1ap8lfjQuIwm004mQ3TPxo+ph1yvRrlQ==";

	// Another CheatEngine.SDK build loaded next to this Client: a prerelease of the next major.
	private const string OtherSdkInformationalVersion = "3.0.0-alpha.0.1+0123456789abcdef0123456789abcdef01234567";

	[Fact]
	public void SnapshotReportsOnlyTheObservedCeLineAndIndependentCapabilities()
	{
		InlineDispatcher dispatcher = new();
		FakeRuntimeProbe probe = new()
		{
			ReportedVersion = 7.7d,
			SystemArchitectureCode = 1,
			TargetAbiCode = 0,
			OpenedProcessId = 42,
			TargetIs64BitValue = true
		};
		RuntimeClient runtime = new(dispatcher, probe, static () => 84, new Version(1, 0, 0), new Version(2, 0, 0));

		bool succeeded =
			runtime.TryGetSnapshot(out CheatEngineRuntimeSnapshot snapshot, out CheatEngineFailure failure,
				TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.Equal(84, snapshot.Epoch);
		Assert.Equal(7.7d, snapshot.ObservedCheatEngineVersion);
		Assert.Equal(new CheatEngineVersion(7, 7, 0, 10621), snapshot.QualifiedCheatEngineBaseline);
		Assert.Equal(CheatEngineArchitecture.X64, snapshot.SystemArchitecture);
		Assert.Equal(CheatEngineArchitecture.X64, snapshot.TargetArchitecture);
		Assert.Equal(PointerSize.Bit64, snapshot.TargetPointerSize);
		Assert.Equal(TargetAbi.Windows, snapshot.TargetAbi);
		Assert.True(snapshot.SdkCapabilities.TryGet(RuntimeCapabilityId.CheatEngineVersion,
			out RuntimeCapabilityAvailability versionCapability));
		Assert.Equal(RuntimeCapabilityAvailabilityState.Available, versionCapability.State);
		Assert.True(snapshot.SdkCapabilities.TryGet(RuntimeCapabilityId.TargetArchitecture,
			out RuntimeCapabilityAvailability targetCapability));
		Assert.Equal(RuntimeCapabilityAvailabilityState.Available, targetCapability.State);
		Assert.Equal(1, dispatcher.InvocationCount);
	}

	[Fact]
	public void SnapshotMarksOnlyTheUnavailableProbeUnavailableWithoutInventingItsValue()
	{
		FakeRuntimeProbe probe = new()
		{
			ReportedVersion = 7.7d,
			SystemArchitectureCode = 1,
			TargetAbiCode = 0,
			OpenedProcessId = 42,
			TargetIs64BitException = new EngineGlobalUnavailableException("Runtime.TargetArchitecture")
		};
		RuntimeClient runtime = new(new InlineDispatcher(), probe, static () => 1);

		CheatEngineRuntimeSnapshot snapshot = runtime.GetSnapshot(TestContext.Current.CancellationToken);

		Assert.Equal(CheatEngineArchitecture.Unknown, snapshot.TargetArchitecture);
		Assert.Equal(PointerSize.Unknown, snapshot.TargetPointerSize);
		Assert.True(snapshot.SdkCapabilities.TryGet(RuntimeCapabilityId.TargetArchitecture,
			out RuntimeCapabilityAvailability targetCapability));
		Assert.Equal(RuntimeCapabilityAvailabilityState.Unavailable, targetCapability.State);
		Assert.True(snapshot.SdkCapabilities.TryGet(RuntimeCapabilityId.SystemArchitecture,
			out RuntimeCapabilityAvailability systemCapability));
		Assert.Equal(RuntimeCapabilityAvailabilityState.Available, systemCapability.State);
	}

	[Fact]
	public void SnapshotLeavesTargetArchitectureUnknownWhenNoTargetIsSelectedWithoutCallingTargetProbe()
	{
		FakeRuntimeProbe probe = new()
		{
			ReportedVersion = 7.7d,
			SystemArchitectureCode = 1,
			TargetAbiCode = 0,
			OpenedProcessId = 0
		};
		RuntimeClient runtime = new(new InlineDispatcher(), probe, static () => 1);

		CheatEngineRuntimeSnapshot snapshot = runtime.GetSnapshot(TestContext.Current.CancellationToken);

		Assert.Equal(CheatEngineArchitecture.Unknown, snapshot.TargetArchitecture);
		Assert.True(snapshot.SdkCapabilities.TryGet(RuntimeCapabilityId.TargetArchitecture,
			out RuntimeCapabilityAvailability targetCapability));
		Assert.Equal(RuntimeCapabilityAvailabilityState.Unknown, targetCapability.State);
		Assert.Equal(0, probe.TargetIs64BitCallCount);
	}

	[Fact]
	public void TryGetSnapshotObservesCancellationBeforeDispatchingAProbe()
	{
		InlineDispatcher dispatcher = new();
		RuntimeClient runtime = new(dispatcher, new FakeRuntimeProbe(), static () => 1);
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();

		bool succeeded = runtime.TryGetSnapshot(out CheatEngineRuntimeSnapshot snapshot, out CheatEngineFailure failure,
			cancellation.Token);

		Assert.False(succeeded);
		Assert.Equal(default, snapshot);
		Assert.Equal(CheatEngineFailureKind.Cancelled, failure.Kind);
		Assert.Equal(0, dispatcher.InvocationCount);
	}

	[Fact]
	public void TryGetSdkCapabilityReturnsUnknownForAnIdentifierThatWasNotProbed()
	{
		RuntimeClient runtime = new(new InlineDispatcher(), new FakeRuntimeProbe(), static () => 1);
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
	public void SnapshotReportsClientGatesWithoutClaimingUnprobedDomainsAreAvailable()
	{
		RuntimeClient runtime = new(new InlineDispatcher(), new FakeRuntimeProbe(), static () => 1);

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

	[Fact]
	public void SnapshotReportsUnsafeLuaPolicyWithoutTreatingItAsHostEvidence()
	{
		RuntimeClient runtime = new(
			new InlineDispatcher(),
			new FakeRuntimeProbe(),
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
	public void SnapshotClassifiesAnInvalidVersionAsMalformedWithoutCallingItUnavailable()
	{
		RuntimeClient runtime = new(
			new InlineDispatcher(),
			new FakeRuntimeProbe { ReportedVersion = double.NaN },
			static () => 1);

		CheatEngineRuntimeSnapshot snapshot = runtime.GetSnapshot(TestContext.Current.CancellationToken);

		Assert.Null(snapshot.ObservedCheatEngineVersion);
		Assert.True(snapshot.SdkCapabilities.TryGet(RuntimeCapabilityId.CheatEngineVersion,
			out RuntimeCapabilityAvailability availability));
		Assert.Equal(RuntimeCapabilityAvailabilityState.Unknown, availability.State);
	}

	[Fact]
	public void SnapshotSeparatesMissingFaultedAndMalformedOpenedProcessEvidence()
	{
		AssertOpenedProcessEvidence(
			new FakeRuntimeProbe { OpenedProcessException = new EngineGlobalUnavailableException("Runtime.Process") },
			ClientCapabilityEvidenceState.Missing,
			ClientCapabilityAvailabilityState.Unavailable);
		AssertOpenedProcessEvidence(
			new FakeRuntimeProbe { OpenedProcessException = new EngineOperationFailedException("Runtime.Process") },
			ClientCapabilityEvidenceState.Faulted,
			ClientCapabilityAvailabilityState.Unknown);
		AssertOpenedProcessEvidence(
			new FakeRuntimeProbe { OpenedProcessId = -1 },
			ClientCapabilityEvidenceState.Malformed,
			ClientCapabilityAvailabilityState.Unknown);
	}

	[Fact]
	public void SnapshotReportsAnInactiveActivationAsALifetimeGate()
	{
		RuntimeClient runtime = new(new InlineDispatcher(), new FakeRuntimeProbe(), static () => 7,
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
		RuntimeClient runtime = new(new InlineDispatcher(), new FakeRuntimeProbe(), static () => 7);

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
		RuntimeClient runtime = new(dispatcher, new FakeRuntimeProbe(), static () => 7);

		Assert.Throws<ArgumentException>(() => runtime.TryGetSdkCapability(
			default, out _, out _, TestContext.Current.CancellationToken));
		Assert.Throws<ArgumentException>(() => runtime.TryGetClientCapability(
			default, out _, out _, TestContext.Current.CancellationToken));
		Assert.Equal(0, dispatcher.InvocationCount);
	}

	[Fact]
	public void SnapshotMarksUnavailableAndDetachedHostProbesWithoutInventingRuntimeFacts()
	{
		RuntimeClient runtime = new(
			new InlineDispatcher(),
			new FakeRuntimeProbe
			{
				VersionException = new EngineGlobalUnavailableException("Runtime.Version"),
				SystemArchitectureException =
					new EngineCapabilityUnavailableException("Runtime.SystemArchitecture"),
				TargetAbiException = new EngineGlobalUnavailableException("Runtime.TargetAbi"),
				OpenedProcessException = new EngineCapabilityUnavailableException("Runtime.OpenedProcess")
			},
			static () => 7);

		CheatEngineRuntimeSnapshot snapshot = runtime.GetSnapshot(TestContext.Current.CancellationToken);

		Assert.Null(snapshot.ObservedCheatEngineVersion);
		Assert.Equal(CheatEngineArchitecture.Unknown, snapshot.SystemArchitecture);
		Assert.Equal(TargetAbi.Unknown, snapshot.TargetAbi);
		Assert.Equal(CheatEngineArchitecture.Unknown, snapshot.TargetArchitecture);
		Assert.True(snapshot.SdkCapabilities.TryGet(RuntimeCapabilityId.CheatEngineVersion,
			out RuntimeCapabilityAvailability version));
		Assert.Equal(RuntimeCapabilityAvailabilityState.Unavailable, version.State);
		Assert.True(snapshot.SdkCapabilities.TryGet(RuntimeCapabilityId.SystemArchitecture,
			out RuntimeCapabilityAvailability system));
		Assert.Equal(RuntimeCapabilityAvailabilityState.Unavailable, system.State);
	}

	[Fact]
	[Trait("Qualification", "Q32")]
	public void SnapshotDerivesX64FromTheX86FamilyAndTheSixtyFourBitFact()
	{
		// Spike C3 D2: on a real x64 target Cheat Engine reports the x86 family, 64-bit and pointer size 8.
		FakeRuntimeProbe probe = new()
		{
			OpenedProcessId = 42,
			TargetIsX86Value = true,
			TargetIsArmValue = false,
			TargetIs64BitValue = true,
			ConfiguredPointerSizeValue = 8
		};
		RuntimeClient runtime = new(new InlineDispatcher(), probe, static () => 1);

		CheatEngineRuntimeSnapshot snapshot = runtime.GetSnapshot(TestContext.Current.CancellationToken);

		Assert.Equal(CheatEngineArchitecture.X64, snapshot.TargetArchitecture);
		Assert.Equal(PointerSize.Bit64, snapshot.TargetPointerSize);
		Assert.Equal(PointerSize.Bit64, snapshot.ConfiguredPointerSize);
		Assert.Equal(8, snapshot.Platform.ConfiguredPointerSizeBytes);
		Assert.False(snapshot.Platform.ConfiguredPointerSizeDiffersFromTargetPointerSize);
		Assert.Equal(RuntimeCapabilityAvailabilityState.Available,
			snapshot.SdkCapabilities.GetState(RuntimeCapabilityId.TargetArchitecture));
	}

	[Theory]
	[Trait("Qualification", "Q32")]
	[InlineData(true, false, true, CheatEngineArchitecture.X64)]
	[InlineData(true, false, false, CheatEngineArchitecture.X86)]
	[InlineData(false, true, true, CheatEngineArchitecture.Arm64)]
	[InlineData(false, true, false, CheatEngineArchitecture.Arm32)]
	[InlineData(true, true, true, CheatEngineArchitecture.Unknown)]
	[InlineData(true, true, false, CheatEngineArchitecture.Unknown)]
	[InlineData(false, false, true, CheatEngineArchitecture.Unknown)]
	[InlineData(false, false, false, CheatEngineArchitecture.Unknown)]
	public void SnapshotDerivesTheIsaFromEveryFamilyCombination(bool isX86, bool isArm, bool is64Bit,
		CheatEngineArchitecture expected)
	{
		FakeRuntimeProbe probe = new()
		{
			OpenedProcessId = 42,
			TargetIsX86Value = isX86,
			TargetIsArmValue = isArm,
			TargetIs64BitValue = is64Bit,
			ConfiguredPointerSizeValue = is64Bit ? 8 : 4
		};
		RuntimeClient runtime = new(new InlineDispatcher(), probe, static () => 1);

		CheatEngineRuntimeSnapshot snapshot = runtime.GetSnapshot(TestContext.Current.CancellationToken);

		Assert.Equal(expected, snapshot.TargetArchitecture);
		Assert.Equal(is64Bit ? PointerSize.Bit64 : PointerSize.Bit32, snapshot.TargetPointerSize);
		Assert.Equal(
			expected == CheatEngineArchitecture.Unknown
				? RuntimeCapabilityAvailabilityState.Unknown
				: RuntimeCapabilityAvailabilityState.Available,
			snapshot.SdkCapabilities.GetState(RuntimeCapabilityId.TargetArchitecture));
	}

	[Fact]
	[Trait("Qualification", "Q32")]
	public void SnapshotKeepsAnUnknownIsaWithAKnownProcessWidthWhenAFamilyProbeIsUnavailable()
	{
		FakeRuntimeProbe probe = new()
		{
			OpenedProcessId = 42,
			TargetIsX86Exception = new LuaException("targetIsX86 failed")
		};
		RuntimeClient runtime = new(new InlineDispatcher(), probe, static () => 1);

		bool succeeded = runtime.TryGetSnapshot(out CheatEngineRuntimeSnapshot snapshot,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.Equal(CheatEngineArchitecture.Unknown, snapshot.TargetArchitecture);
		Assert.Equal(PointerSize.Bit64, snapshot.TargetPointerSize);
		Assert.Equal(RuntimeCapabilityAvailabilityState.Unknown,
			snapshot.SdkCapabilities.GetState(RuntimeCapabilityId.TargetArchitecture));
	}

	[Fact]
	[Trait("Qualification", "Q32")]
	public void SnapshotNeverDerivesX64FromTheSixtyFourBitFactAlone()
	{
		FakeRuntimeProbe probe = new()
		{
			OpenedProcessId = 42,
			TargetIs64BitValue = true,
			TargetIsX86Exception = new EngineGlobalUnavailableException("targetIsX86"),
			TargetIsArmException = new EngineGlobalUnavailableException("targetIsArm")
		};
		RuntimeClient runtime = new(new InlineDispatcher(), probe, static () => 1);

		CheatEngineRuntimeSnapshot snapshot = runtime.GetSnapshot(TestContext.Current.CancellationToken);

		Assert.Equal(CheatEngineArchitecture.Unknown, snapshot.TargetArchitecture);
		Assert.Equal(PointerSize.Bit64, snapshot.TargetPointerSize);
		Assert.Equal(RuntimeCapabilityAvailabilityState.Unavailable,
			snapshot.SdkCapabilities.GetState(RuntimeCapabilityId.TargetArchitecture));
	}

	[Theory]
	[InlineData(1, TargetAbi.Unix, RuntimeCapabilityAvailabilityState.Available)]
	[InlineData(7, TargetAbi.Unknown, RuntimeCapabilityAvailabilityState.Unknown)]
	public void SnapshotDerivesTheIsaIndependentlyOfTheAbi(int abiCode, TargetAbi expectedAbi,
		RuntimeCapabilityAvailabilityState expectedAbiState)
	{
		FakeRuntimeProbe probe = new()
		{
			OpenedProcessId = 42,
			TargetAbiCode = abiCode
		};
		RuntimeClient runtime = new(new InlineDispatcher(), probe, static () => 1);

		CheatEngineRuntimeSnapshot snapshot = runtime.GetSnapshot(TestContext.Current.CancellationToken);

		Assert.Equal(CheatEngineArchitecture.X64, snapshot.TargetArchitecture);
		Assert.Equal(PointerSize.Bit64, snapshot.TargetPointerSize);
		Assert.Equal(expectedAbi, snapshot.TargetAbi);
		Assert.Equal(expectedAbiState, snapshot.SdkCapabilities.GetState(RuntimeCapabilityId.TargetAbi));
		Assert.Equal(RuntimeCapabilityAvailabilityState.Available,
			snapshot.SdkCapabilities.GetState(RuntimeCapabilityId.TargetArchitecture));
	}

	[Fact]
	[Trait("Qualification", "Q31")]
	public void SnapshotKeepsAConfiguredPointerSizeOfFourSeparateFromAnX64Target()
	{
		// Spike C3 D3: setPointerSize(4) on an x64 target leaves targetIs64Bit true and readPointer 8 bytes wide.
		FakeRuntimeProbe probe = new()
		{
			OpenedProcessId = 42,
			ConfiguredPointerSizeValue = 4
		};
		RuntimeClient runtime = new(new InlineDispatcher(), probe, static () => 1);

		bool succeeded = runtime.TryGetSnapshot(out CheatEngineRuntimeSnapshot snapshot,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.Equal(CheatEngineArchitecture.X64, snapshot.TargetArchitecture);
		Assert.Equal(PointerSize.Bit64, snapshot.TargetPointerSize);
		Assert.Equal(PointerSize.Bit32, snapshot.ConfiguredPointerSize);
		Assert.Equal(4, snapshot.Platform.ConfiguredPointerSizeBytes);
		Assert.True(snapshot.Platform.ConfiguredPointerSizeDiffersFromTargetPointerSize);
		Assert.Equal(RuntimeCapabilityAvailabilityState.Available,
			snapshot.SdkCapabilities.GetState(RuntimeClient.ConfiguredPointerSizeCapability));
	}

	[Fact]
	[Trait("Qualification", "Q31")]
	public void SnapshotKeepsARawConfiguredPointerSizeOutsideFourAndEight()
	{
		// Spike C3 D3(b): setPointerSize accepts any integer; 2 was stored and read back.
		FakeRuntimeProbe probe = new()
		{
			OpenedProcessId = 42,
			ConfiguredPointerSizeValue = 2
		};
		RuntimeClient runtime = new(new InlineDispatcher(), probe, static () => 1);

		CheatEngineRuntimeSnapshot snapshot = runtime.GetSnapshot(TestContext.Current.CancellationToken);

		Assert.Equal(2, snapshot.Platform.ConfiguredPointerSizeBytes);
		Assert.Equal(PointerSize.Unknown, snapshot.ConfiguredPointerSize);
		Assert.Equal(PointerSize.Bit64, snapshot.TargetPointerSize);
		Assert.True(snapshot.Platform.ConfiguredPointerSizeDiffersFromTargetPointerSize);
		Assert.Equal(RuntimeCapabilityAvailabilityState.Unknown,
			snapshot.SdkCapabilities.GetState(RuntimeClient.ConfiguredPointerSizeCapability));
	}

	[Fact]
	[Trait("Qualification", "Q32")]
	public void SnapshotReadsNoTargetFactWithoutASelectedProcess()
	{
		// Spike C3 D2: with no target opened Cheat Engine reports x86, 64-bit and pointer size 8, so nothing is read.
		FakeRuntimeProbe probe = new()
		{
			OpenedProcessId = 0
		};
		RuntimeClient runtime = new(new InlineDispatcher(), probe, static () => 1);

		CheatEngineRuntimeSnapshot snapshot = runtime.GetSnapshot(TestContext.Current.CancellationToken);

		Assert.Equal(0, probe.Count(nameof(IRuntimeProbe.TargetIs64Bit)));
		Assert.Equal(0, probe.Count(nameof(IRuntimeProbe.TargetIsX86)));
		Assert.Equal(0, probe.Count(nameof(IRuntimeProbe.TargetIsArm)));
		Assert.Equal(0, probe.Count(nameof(IRuntimeProbe.GetConfiguredPointerSize)));
		Assert.Equal(1, probe.Count(nameof(IRuntimeProbe.GetOpenedProcessId)));
		Assert.Equal(CheatEngineArchitecture.Unknown, snapshot.TargetArchitecture);
		Assert.Equal(PointerSize.Unknown, snapshot.TargetPointerSize);
		Assert.Null(snapshot.Platform.ConfiguredPointerSizeBytes);
		Assert.Null(snapshot.Platform.ConfiguredPointerSizeDiffersFromTargetPointerSize);
		Assert.Equal(RuntimeCapabilityAvailabilityState.Unknown,
			snapshot.SdkCapabilities.GetState(RuntimeClient.ConfiguredPointerSizeCapability));
	}

	[Fact]
	[Trait("Qualification", "Q32")]
	public void SnapshotDiscardsTargetFactsWhenThePidChangesDuringObservation()
	{
		FakeRuntimeProbe probe = new()
		{
			OpenedProcessId = 42,
			LaterOpenedProcessIds = [43]
		};
		RuntimeClient runtime = new(new InlineDispatcher(), probe, static () => 1);

		CheatEngineRuntimeSnapshot snapshot = runtime.GetSnapshot(TestContext.Current.CancellationToken);

		Assert.Equal(2, probe.Count(nameof(IRuntimeProbe.GetOpenedProcessId)));
		Assert.Equal(CheatEngineArchitecture.Unknown, snapshot.TargetArchitecture);
		Assert.Equal(PointerSize.Unknown, snapshot.TargetPointerSize);
		Assert.Null(snapshot.Platform.ConfiguredPointerSizeBytes);
		Assert.Equal(RuntimeCapabilityAvailabilityState.Unknown,
			snapshot.SdkCapabilities.GetState(RuntimeCapabilityId.TargetArchitecture));
		Assert.Equal(RuntimeCapabilityAvailabilityState.Unknown,
			snapshot.SdkCapabilities.GetState(RuntimeClient.ConfiguredPointerSizeCapability));
	}

	[Theory]
	[InlineData(nameof(IRuntimeProbe.GetCheatEngineVersion))]
	[InlineData(nameof(IRuntimeProbe.GetSystemArchitecture))]
	[InlineData(nameof(IRuntimeProbe.GetTargetAbi))]
	[InlineData(nameof(IRuntimeProbe.GetOpenedProcessId))]
	[InlineData(nameof(IRuntimeProbe.TargetIs64Bit))]
	[InlineData(nameof(IRuntimeProbe.TargetIsX86))]
	[InlineData(nameof(IRuntimeProbe.TargetIsArm))]
	[InlineData(nameof(IRuntimeProbe.GetConfiguredPointerSize))]
	public void SnapshotClassifiesLuaExceptionsFromGeneratedBindingsAsFaultedWithoutEscaping(string member)
	{
		// The single failure type of a throwing-form generated binding: undefined global, raised Lua error and
		// unexpected result type all surface as LuaException.
		LuaException fault = new("generated binding failure");
		FakeRuntimeProbe probe = new()
		{
			OpenedProcessId = 42,
			ThrowOnce = true,
			VersionException = member == nameof(IRuntimeProbe.GetCheatEngineVersion) ? fault : null,
			SystemArchitectureException = member == nameof(IRuntimeProbe.GetSystemArchitecture) ? fault : null,
			TargetAbiException = member == nameof(IRuntimeProbe.GetTargetAbi) ? fault : null,
			OpenedProcessException = member == nameof(IRuntimeProbe.GetOpenedProcessId) ? fault : null,
			TargetIs64BitException = member == nameof(IRuntimeProbe.TargetIs64Bit) ? fault : null,
			TargetIsX86Exception = member == nameof(IRuntimeProbe.TargetIsX86) ? fault : null,
			TargetIsArmException = member == nameof(IRuntimeProbe.TargetIsArm) ? fault : null,
			ConfiguredPointerSizeException = member == nameof(IRuntimeProbe.GetConfiguredPointerSize) ? fault : null
		};
		RuntimeClient runtime = new(new InlineDispatcher(), probe, static () => 1);
		RuntimeCapabilityId affected = member switch
		{
			nameof(IRuntimeProbe.GetCheatEngineVersion) => RuntimeCapabilityId.CheatEngineVersion,
			nameof(IRuntimeProbe.GetSystemArchitecture) => RuntimeCapabilityId.SystemArchitecture,
			nameof(IRuntimeProbe.GetTargetAbi) => RuntimeCapabilityId.TargetAbi,
			nameof(IRuntimeProbe.GetConfiguredPointerSize) => RuntimeClient.ConfiguredPointerSizeCapability,
			_ => RuntimeCapabilityId.TargetArchitecture
		};

		bool succeeded = runtime.TryGetSnapshot(out CheatEngineRuntimeSnapshot faulted,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.Equal(RuntimeCapabilityAvailabilityState.Unknown, faulted.SdkCapabilities.GetState(affected));
		if (member == nameof(IRuntimeProbe.GetOpenedProcessId))
		{
			Assert.True(faulted.ClientCapabilities.TryGet(ClientCapabilityId.ProcessSelection,
				out ClientCapabilityAvailability processSelection));
			Assert.Equal(ClientCapabilityEvidenceState.Faulted, processSelection.Evidence.Host.State);
			Assert.Equal(ProbeClassifier.IndistinguishableLuaFailureReason, processSelection.Evidence.Host.Reason);
		}

		// The public SDK capability entry carries a state only, so the evidence each probe produced is replayed through
		// the same classification path the snapshot uses (the observer for target facts, the classifier for host facts).
		ClientCapabilityEvidenceGate evidence = ReplayProbeEvidence(member, new LuaException("generated binding failure"));
		Assert.Equal(ClientCapabilityEvidenceState.Faulted, evidence.State);
		Assert.Equal(ProbeClassifier.IndistinguishableLuaFailureReason, evidence.Reason);
		Assert.Contains("undefined global", evidence.Reason, StringComparison.Ordinal);
		Assert.Contains("raised Lua error", evidence.Reason, StringComparison.Ordinal);
		Assert.Contains("unexpected result type", evidence.Reason, StringComparison.Ordinal);

		// Recovery (binding definition of done A9): the next snapshot on the same host succeeds with every fact.
		CheatEngineRuntimeSnapshot recovered = runtime.GetSnapshot(TestContext.Current.CancellationToken);

		Assert.Equal(CheatEngineArchitecture.X64, recovered.TargetArchitecture);
		Assert.Equal(RuntimeCapabilityAvailabilityState.Available, recovered.SdkCapabilities.GetState(affected));
	}

	/// <summary>Replays one probe member throwing <paramref name="fault" /> through the snapshot's classification path.</summary>
	private static ClientCapabilityEvidenceGate ReplayProbeEvidence(string member, LuaException fault)
	{
		FakeRuntimeProbe probe = new()
		{
			OpenedProcessId = 42,
			VersionException = member == nameof(IRuntimeProbe.GetCheatEngineVersion) ? fault : null,
			SystemArchitectureException = member == nameof(IRuntimeProbe.GetSystemArchitecture) ? fault : null,
			TargetAbiException = member == nameof(IRuntimeProbe.GetTargetAbi) ? fault : null,
			OpenedProcessException = member == nameof(IRuntimeProbe.GetOpenedProcessId) ? fault : null,
			TargetIs64BitException = member == nameof(IRuntimeProbe.TargetIs64Bit) ? fault : null,
			TargetIsX86Exception = member == nameof(IRuntimeProbe.TargetIsX86) ? fault : null,
			TargetIsArmException = member == nameof(IRuntimeProbe.TargetIsArm) ? fault : null,
			ConfiguredPointerSizeException = member == nameof(IRuntimeProbe.GetConfiguredPointerSize) ? fault : null
		};
		ObservedTargetArchitecture facts = TargetArchitectureObserver.Observe(probe, readConfiguredPointerSize: true);
		return member switch
		{
			nameof(IRuntimeProbe.GetCheatEngineVersion) => ProbeClassifier.Probe(probe.GetCheatEngineVersion).Evidence,
			nameof(IRuntimeProbe.GetSystemArchitecture) => ProbeClassifier.Probe(probe.GetSystemArchitecture).Evidence,
			nameof(IRuntimeProbe.GetTargetAbi) => ProbeClassifier.Probe(probe.GetTargetAbi).Evidence,
			nameof(IRuntimeProbe.GetOpenedProcessId) => facts.ProcessId.Evidence,
			nameof(IRuntimeProbe.TargetIs64Bit) => facts.Is64Bit.Evidence,
			nameof(IRuntimeProbe.TargetIsX86) => facts.IsX86Family.Evidence,
			nameof(IRuntimeProbe.TargetIsArm) => facts.IsArmFamily.Evidence,
			nameof(IRuntimeProbe.GetConfiguredPointerSize) => facts.ConfiguredPointerSize.Evidence,
			_ => throw new ArgumentOutOfRangeException(nameof(member), member, null)
		};
	}

	[Fact]
	[Trait("Qualification", "Q31")]
	public void SnapshotReportsTheConfiguredPointerSizeCapabilityWithTheSdkTwoIdentifier()
	{
		RuntimeClient runtime = new(new InlineDispatcher(), new FakeRuntimeProbe { OpenedProcessId = 42 },
			static () => 1);

		CheatEngineRuntimeSnapshot snapshot = runtime.GetSnapshot(TestContext.Current.CancellationToken);

		Assert.Equal("Runtime.ConfiguredPointerSize", RuntimeClient.ConfiguredPointerSizeCapability.Value);
		Assert.Equal(RuntimeClient.ConfiguredPointerSizeCapabilityValue, RuntimeCapabilityId.ConfiguredPointerSize.Value);
		Assert.True(snapshot.SdkCapabilities.TryGet(new RuntimeCapabilityId("Runtime.ConfiguredPointerSize"),
			out RuntimeCapabilityAvailability availability));
		Assert.Equal(RuntimeCapabilityAvailabilityState.Available, availability.State);
	}

	[Fact]
	public void SnapshotReturnsADetachedRuntimeFaultAsAFailureInsteadOfThrowing()
	{
		// A detached SDK runtime throws InvalidOperationException, which the probe classifier deliberately does not
		// catch; the SdkBoundary rule still keeps it from crossing the Try method (F15).
		InvalidOperationException detached = new("The plugin is not enabled.");
		RuntimeClient runtime = new(new InlineDispatcher(),
			new FakeRuntimeProbe { VersionException = detached }, static () => 1);

		bool succeeded = runtime.TryGetSnapshot(out CheatEngineRuntimeSnapshot snapshot,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, snapshot);
		Assert.Equal("Runtime.GetSnapshot", failure.Operation);
		Assert.Same(detached, failure.Exception);
	}

	[Fact]
	public void PackageGateIsSatisfiedWhenTheLoadedSdkMatchesTheEmbeddedIdentity()
	{
		ConsumedSdkIdentity identity = new(SdkVersion, SdkCommit, SdkContentHash, $"{SdkVersion}+{SdkCommit}");

		ClientCapabilityAvailability typedMemory = GetClientCapability(identity, ClientCapabilityId.TypedMemory);

		Assert.Equal(ClientCapabilityEvidenceState.Satisfied, typedMemory.Evidence.Package.State);
		Assert.Contains($"{SdkVersion}+{SdkCommit}", typedMemory.Evidence.Package.Reason, StringComparison.Ordinal);
		Assert.Contains(SdkContentHash, typedMemory.Evidence.Package.Reason, StringComparison.Ordinal);
		foreach (ClientCapabilityId operational in OperationalCapabilities)
		{
			Assert.Equal(ClientCapabilityEvidenceState.Satisfied,
				GetClientCapability(identity, operational).Evidence.Package.State);
		}
	}

	[Fact]
	public void PackageGateIsMissingWhenTheLoadedSdkDiffersFromTheEmbeddedIdentity()
	{
		ConsumedSdkIdentity identity = new(SdkVersion, SdkCommit, SdkContentHash, OtherSdkInformationalVersion);

		ClientCapabilityAvailability typedMemory = GetClientCapability(identity, ClientCapabilityId.TypedMemory);

		Assert.Equal(ClientCapabilityEvidenceState.Missing, typedMemory.Evidence.Package.State);
		Assert.Equal(ClientCapabilityAvailabilityState.Unavailable, typedMemory.State);
		Assert.Contains(OtherSdkInformationalVersion, typedMemory.Evidence.Package.Reason, StringComparison.Ordinal);
		Assert.Contains("refuses to treat it as its SDK", typedMemory.Evidence.Package.Reason, StringComparison.Ordinal);
	}

	[Fact]
	public void PackageGateIsUnknownWithoutEmbeddedIdentity()
	{
		ConsumedSdkIdentity identity = new(null, null, null, $"{SdkVersion}+{SdkCommit}");

		ClientCapabilityAvailability typedMemory = GetClientCapability(identity, ClientCapabilityId.TypedMemory);

		Assert.False(identity.IsEmbedded);
		Assert.Equal(ClientCapabilityEvidenceState.Unknown, typedMemory.Evidence.Package.State);
		Assert.Contains("embeds no consumed CheatEngine.SDK identity", typedMemory.Evidence.Package.Reason,
			StringComparison.Ordinal);
	}

	[Fact]
	public void PackageGateIsUnknownWhenTheLoadedSdkDeclaresNoInformationalVersion()
	{
		ConsumedSdkIdentity identity = new(SdkVersion, SdkCommit, SdkContentHash, null);

		ClientCapabilityAvailability typedMemory = GetClientCapability(identity, ClientCapabilityId.TypedMemory);

		Assert.Equal(ClientCapabilityEvidenceState.Unknown, typedMemory.Evidence.Package.State);
		Assert.Contains("declares no informational version", typedMemory.Evidence.Package.Reason,
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
		ConsumedSdkIdentity differentPackage = new(SdkVersion, SdkCommit, SdkContentHash, OtherSdkInformationalVersion);

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
		RuntimeClient runtime = new(new InlineDispatcher(), new FakeRuntimeProbe { OpenedProcessId = 42 },
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
		RuntimeClient runtime = new(new InlineDispatcher(), new FakeRuntimeProbe { OpenedProcessId = 42 },
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
	public void IRuntimeProbeExposesOnlyObservationMembers()
	{
		// Q45 / ADR-09a: a runtime probe observes; it never opens a process, loads a table or changes a host setting.
		string[] allowed =
		[
			nameof(IRuntimeProbe.GetCheatEngineVersion), nameof(IRuntimeProbe.GetSystemArchitecture),
			nameof(IRuntimeProbe.GetTargetAbi), nameof(ITargetArchitectureProbe.GetOpenedProcessId),
			nameof(ITargetArchitectureProbe.TargetIs64Bit), nameof(ITargetArchitectureProbe.TargetIsX86),
			nameof(ITargetArchitectureProbe.TargetIsArm), nameof(ITargetArchitectureProbe.GetConfiguredPointerSize)
		];
		System.Reflection.MethodInfo[] members =
		[
			.. typeof(IRuntimeProbe).GetMethods(),
			.. typeof(IRuntimeProbe).GetInterfaces().SelectMany(static inherited => inherited.GetMethods())
		];

		Assert.Equal(allowed.Order(StringComparer.Ordinal),
			members.Select(static member => member.Name).Order(StringComparer.Ordinal));
		Assert.All(members, static member =>
		{
			Assert.NotEqual(typeof(void), member.ReturnType);
			Assert.Empty(member.GetParameters());
		});
		Assert.Empty(typeof(IRuntimeProbe).GetProperties());
		Assert.Empty(typeof(IRuntimeProbe).GetEvents());
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
		return new ConsumedSdkIdentity(SdkVersion, SdkCommit, SdkContentHash, $"{SdkVersion}+{SdkCommit}");
	}

	private static ClientCapabilityAvailability GetClientCapability(ConsumedSdkIdentity identity,
		ClientCapabilityId capability)
	{
		RuntimeClient runtime = new(new InlineDispatcher(), new FakeRuntimeProbe { OpenedProcessId = 42 },
			static () => 1, sdkIdentity: identity);
		CheatEngineRuntimeSnapshot snapshot = runtime.GetSnapshot(TestContext.Current.CancellationToken);
		Assert.True(snapshot.ClientCapabilities.TryGet(capability, out ClientCapabilityAvailability availability));
		return availability;
	}

	private static void AssertOpenedProcessEvidence(
		FakeRuntimeProbe probe,
		ClientCapabilityEvidenceState expectedHostState,
		ClientCapabilityAvailabilityState expectedAvailabilityState)
	{
		RuntimeClient runtime = new(new InlineDispatcher(), probe, static () => 7);

		CheatEngineRuntimeSnapshot snapshot = runtime.GetSnapshot(TestContext.Current.CancellationToken);

		Assert.True(snapshot.ClientCapabilities.TryGet(ClientCapabilityId.ProcessSelection,
			out ClientCapabilityAvailability availability));
		Assert.Equal(expectedHostState, availability.Evidence.Host.State);
		Assert.Equal(expectedAvailabilityState, availability.State);
	}

	private sealed class FakeRuntimeProbe : IRuntimeProbe
	{
		private readonly HashSet<string> _thrown = new(StringComparer.Ordinal);
		private int _openedProcessIdReads;

		internal double ReportedVersion
		{
			get;
			init;
		} = 7.7d;

		internal int SystemArchitectureCode
		{
			get;
			init;
		} = 1;

		internal int TargetAbiCode
		{
			get;
			init;
		}

		internal long OpenedProcessId
		{
			get;
			init;
		}

		/// <summary>PIDs returned by the reads that follow the first one; the last entry repeats.</summary>
		internal long[]? LaterOpenedProcessIds
		{
			get;
			init;
		}

		internal bool TargetIs64BitValue
		{
			get;
			init;
		} = true;

		internal bool TargetIsX86Value
		{
			get;
			init;
		} = true;

		internal bool TargetIsArmValue
		{
			get;
			init;
		}

		internal int ConfiguredPointerSizeValue
		{
			get;
			init;
		} = sizeof(ulong);

		internal Exception? TargetIs64BitException
		{
			get;
			init;
		}

		internal Exception? TargetIsX86Exception
		{
			get;
			init;
		}

		internal Exception? TargetIsArmException
		{
			get;
			init;
		}

		internal Exception? ConfiguredPointerSizeException
		{
			get;
			init;
		}

		internal Exception? VersionException
		{
			get;
			init;
		}

		internal Exception? SystemArchitectureException
		{
			get;
			init;
		}

		internal Exception? TargetAbiException
		{
			get;
			init;
		}

		internal Exception? OpenedProcessException
		{
			get;
			init;
		}

		/// <summary>When set, each configured exception is thrown by the first call of its member only.</summary>
		internal bool ThrowOnce
		{
			get;
			init;
		}

		internal List<string> Calls
		{
			get;
		} = [];

		internal int TargetIs64BitCallCount => Count(nameof(TargetIs64Bit));

		public double GetCheatEngineVersion()
		{
			Record(nameof(GetCheatEngineVersion), VersionException);
			return ReportedVersion;
		}

		public int GetSystemArchitecture()
		{
			Record(nameof(GetSystemArchitecture), SystemArchitectureException);
			return SystemArchitectureCode;
		}

		public int GetTargetAbi()
		{
			Record(nameof(GetTargetAbi), TargetAbiException);
			return TargetAbiCode;
		}

		public long GetOpenedProcessId()
		{
			Record(nameof(GetOpenedProcessId), OpenedProcessException);
			int read = _openedProcessIdReads++;
			return read == 0 || LaterOpenedProcessIds is not { Length: > 0 } later
				? OpenedProcessId
				: later[Math.Min(read - 1, later.Length - 1)];
		}

		public bool TargetIs64Bit()
		{
			Record(nameof(TargetIs64Bit), TargetIs64BitException);
			return TargetIs64BitValue;
		}

		public bool TargetIsX86()
		{
			Record(nameof(TargetIsX86), TargetIsX86Exception);
			return TargetIsX86Value;
		}

		public bool TargetIsArm()
		{
			Record(nameof(TargetIsArm), TargetIsArmException);
			return TargetIsArmValue;
		}

		public int GetConfiguredPointerSize()
		{
			Record(nameof(GetConfiguredPointerSize), ConfiguredPointerSizeException);
			return ConfiguredPointerSizeValue;
		}

		internal int Count(string member)
		{
			return Calls.Count(call => call == member);
		}

		private void Record(string member, Exception? exception)
		{
			Calls.Add(member);
			if (exception is not null && (!ThrowOnce || _thrown.Add(member)))
			{
				throw exception;
			}
		}
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
