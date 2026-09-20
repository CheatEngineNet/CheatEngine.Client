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
			ReportedVersion = 7.7d, SystemArchitectureCode = 1, TargetAbiCode = 0, OpenedProcessId = 0
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
		Assert.Contains("live ownership", valueScanning.Reason, StringComparison.OrdinalIgnoreCase);
		Assert.True(snapshot.ClientCapabilities.TryGet(ClientCapabilityId.TypedMemory,
			out ClientCapabilityAvailability typedMemory));
		Assert.Equal(ClientCapabilityAvailabilityState.Unknown, typedMemory.State);
		Assert.True(snapshot.ClientCapabilities.TryGet(ClientCapabilityId.UnsafeLuaExecution,
			out ClientCapabilityAvailability unsafeLua));
		Assert.Equal(ClientCapabilityAvailabilityState.Unavailable, unsafeLua.State);
	}

	[Fact]
	public void SnapshotReportsUnsafeLuaAvailableOnlyForTheExplicitActivationOptIn()
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
		Assert.Equal(ClientCapabilityAvailabilityState.Available, availability.State);
		Assert.True(availability.IsAvailable);
	}

	[Fact]
	public void GetSnapshotThrowsTheClassifiedFailureWhenTheHostReportsAnInvalidVersion()
	{
		RuntimeClient runtime = new(
			new InlineDispatcher(),
			new FakeRuntimeProbe { ReportedVersion = double.NaN },
			static () => 1);

		CheatEngineOperationException exception = Assert.Throws<CheatEngineOperationException>(() =>
			runtime.GetSnapshot(TestContext.Current.CancellationToken));

		Assert.Equal(CheatEngineFailureKind.InvalidHostResult, exception.Failure.Kind);
		Assert.Equal("Dispatcher.Invoke", exception.Failure.Operation);
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

		internal int TargetIs64BitCallCount
		{
			get;
			private set;
		}

		public double GetCheatEngineVersion()
		{
			return ReportedVersion;
		}

		public int GetSystemArchitecture()
		{
			return SystemArchitectureCode;
		}

		public int GetTargetAbi()
		{
			return TargetAbiCode;
		}

		public long GetOpenedProcessId()
		{
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
			try
			{
				callback();
				failure = default;
				return true;
			}
			catch (Exception exception)
			{
				failure = FromException(exception);
				return false;
			}
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
			try
			{
				result = callback();
				failure = default;
				return true;
			}
			catch (Exception exception)
			{
				result = default!;
				failure = FromException(exception);
				return false;
			}
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
			if (TryInvoke(callback, out var result, out var failure, cancellationToken))
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

		private static CheatEngineFailure FromException(Exception exception)
		{
			return new CheatEngineFailure(
				exception is EngineMarshallingException
					? CheatEngineFailureKind.InvalidHostResult
					: CheatEngineFailureKind.OperationRejected,
				"Dispatcher.Invoke",
				exception.Message,
				exception);
		}
	}
}
