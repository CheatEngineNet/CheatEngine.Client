using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Results;
using CheatEngine.Client.Runtime;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Core.Tests.Domains;

public sealed class RuntimeClientTests
{
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
		RuntimeClient runtime = new(dispatcher, probe, static () => 84, new Version(0, 1, 0), new Version(1, 0, 0));

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
		Assert.Equal(ClientCapabilityEvidenceState.Missing, valueScanning.Evidence.Package.State);
		Assert.Contains("unavailable adapter", valueScanning.Reason, StringComparison.OrdinalIgnoreCase);
		Assert.True(snapshot.ClientCapabilities.TryGet(ClientCapabilityId.TypedMemory,
			out ClientCapabilityAvailability typedMemory));
		Assert.Equal(ClientCapabilityAvailabilityState.Unknown, typedMemory.State);
		Assert.True(snapshot.ClientCapabilities.TryGet(ClientCapabilityId.UnsafeLuaExecution,
			out ClientCapabilityAvailability unsafeLua));
		Assert.Equal(ClientCapabilityAvailabilityState.Unavailable, unsafeLua.State);

		ClientCapabilityId[] unavailableCapabilities =
		[
			ClientCapabilityId.Allocations,
			ClientCapabilityId.RemoteExecution,
			ClientCapabilityId.Debugger,
			ClientCapabilityId.Hotkeys,
			ClientCapabilityId.Timers,
			ClientCapabilityId.Dbvm
		];
		foreach (ClientCapabilityId capability in unavailableCapabilities)
		{
			Assert.True(snapshot.ClientCapabilities.TryGet(capability, out ClientCapabilityAvailability availability));
			Assert.Equal(ClientCapabilityAvailabilityState.Unavailable, availability.State);
			Assert.False(availability.IsAvailable);
		}

		ClientCapabilityId[] additionallyUnavailableCapabilities =
		[
			ClientCapabilityId.Assembly,
			ClientCapabilityId.Speed,
			ClientCapabilityId.Hashing
		];
		foreach (ClientCapabilityId capability in additionallyUnavailableCapabilities)
		{
			Assert.True(snapshot.ClientCapabilities.TryGet(capability, out ClientCapabilityAvailability availability));
			Assert.Equal(ClientCapabilityAvailabilityState.Unavailable, availability.State);
			Assert.Equal(ClientCapabilityEvidenceState.Missing, availability.Evidence.Implementation.State);
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

		internal bool TargetIs64BitValue
		{
			get;
			init;
		}

		internal Exception? TargetIs64BitException
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

		internal int TargetIs64BitCallCount
		{
			get;
			private set;
		}

		public double GetCheatEngineVersion()
		{
			if (VersionException is not null)
			{
				throw VersionException;
			}

			return ReportedVersion;
		}

		public int GetSystemArchitecture()
		{
			if (SystemArchitectureException is not null)
			{
				throw SystemArchitectureException;
			}

			return SystemArchitectureCode;
		}

		public int GetTargetAbi()
		{
			if (TargetAbiException is not null)
			{
				throw TargetAbiException;
			}

			return TargetAbiCode;
		}

		public long GetOpenedProcessId()
		{
			if (OpenedProcessException is not null)
			{
				throw OpenedProcessException;
			}

			return OpenedProcessId;
		}

		public bool TargetIs64Bit()
		{
			TargetIs64BitCallCount++;
			if (TargetIs64BitException is not null)
			{
				throw TargetIs64BitException;
			}

			return TargetIs64BitValue;
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
				failure.Throw();
			}
		}

		public T Invoke<T>(Func<T> callback, CancellationToken cancellationToken = default)
		{
			if (TryInvoke(callback, out T result, out CheatEngineFailure failure, cancellationToken))
			{
				return result;
			}

			failure.Throw();
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
