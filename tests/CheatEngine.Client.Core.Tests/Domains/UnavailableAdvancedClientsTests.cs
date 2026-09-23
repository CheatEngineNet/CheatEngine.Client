using CheatEngine.Client.Allocations;
using CheatEngine.Client.Assembly;
using CheatEngine.Client.Core.Domains.Allocations;
using CheatEngine.Client.Core.Domains.Assembly;
using CheatEngine.Client.Core.Domains.Dbvm;
using CheatEngine.Client.Core.Domains.Debugger;
using CheatEngine.Client.Core.Domains.Hashing;
using CheatEngine.Client.Core.Domains.Hotkeys;
using CheatEngine.Client.Core.Domains.RemoteExecution;
using CheatEngine.Client.Core.Domains.Speed;
using CheatEngine.Client.Core.Domains.Timers;
using CheatEngine.Client.Dbvm;
using CheatEngine.Client.Debugger;
using CheatEngine.Client.Events;
using CheatEngine.Client.Hashing;
using CheatEngine.Client.Hotkeys;
using CheatEngine.Client.RemoteExecution;
using CheatEngine.Client.Results;
using CheatEngine.Client.Speed;
using CheatEngine.Client.Timers;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Tests.Domains;

public sealed class UnavailableAdvancedClientsTests
{
	private static readonly Address _address = new(0x401000);

	[Fact]
	[Trait("Qualification", "Q44")]
	public void AllocationTryOperationReportsTheLiveGateAndLeavesNoLease()
	{
		UnavailableAllocationClient client = new();

		bool succeeded = client.TryAllocate(new TargetAllocationRequest(4096), out ITargetMemoryLease? lease,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Null(lease);
		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, failure.Kind);
		Assert.Equal("Allocations.Allocate", failure.Operation);
		Assert.Contains("Cheat Engine 7.7 x64 live gate", failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	[Trait("Qualification", "Q44")]
	public void AllocationThrowingOperationPreservesTheUnavailableFailure()
	{
		UnavailableAllocationClient client = new();

		CheatEngineOperationException exception = Assert.Throws<CheatEngineOperationException>(() =>
		{
			_ = client.Allocate(new TargetAllocationRequest(1), TestContext.Current.CancellationToken);
		});

		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, exception.Failure.Kind);
	}

	[Fact]
	[Trait("Qualification", "Q44")]
	public void AssemblyTryOperationsReturnDefaultsAndAStableCapabilityFailure()
	{
		UnavailableAssemblyClient client = new();

		Assert.False(client.TryDisassemble(_address, out AssemblyInstructionSnapshot instruction,
			out CheatEngineFailure disassemble,
			TestContext.Current.CancellationToken));
		Assert.Equal(default, instruction);
		Assert.Equal("Assembly.Disassemble", disassemble.Operation);

		Assert.False(client.TryGetInstructionSize(_address, out int size, out CheatEngineFailure sizeFailure,
			TestContext.Current.CancellationToken));
		Assert.Equal(0, size);
		Assert.Equal("Assembly.GetInstructionSize", sizeFailure.Operation);

		Assert.False(client.TryGetPreviousInstruction(_address, out Address previous,
			out CheatEngineFailure previousFailure,
			TestContext.Current.CancellationToken));
		Assert.Equal(default, previous);
		Assert.Equal("Assembly.GetPreviousInstruction", previousFailure.Operation);

		Assert.False(client.TryGetComment(_address, out string? comment, out CheatEngineFailure commentFailure,
			TestContext.Current.CancellationToken));
		Assert.Null(comment);
		Assert.Equal("Assembly.GetComment", commentFailure.Operation);

		Assert.False(client.TryAssemble(new AssemblyInstructionRequest(_address, "nop"), out _,
			out CheatEngineFailure assemble,
			TestContext.Current.CancellationToken));
		Assert.Equal("Assembly.Assemble", assemble.Operation);

		Assert.False(client.TryApplyPatch(new AutoAssemblerScript("[ENABLE]\n[DISABLE]"),
			out IAutoAssemblerPatchLease? patch, out CheatEngineFailure patchFailure,
			TestContext.Current.CancellationToken));
		Assert.Null(patch);
		Assert.Equal("Assembly.ApplyPatch", patchFailure.Operation);
	}

	[Fact]
	[Trait("Qualification", "Q44")]
	public void AssemblyThrowingOperationPreservesTheUnavailableFailure()
	{
		UnavailableAssemblyClient client = new();

		CheatEngineOperationException exception = Assert.Throws<CheatEngineOperationException>(() =>
		{
			_ = client.Disassemble(_address, TestContext.Current.CancellationToken);
		});

		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, exception.Failure.Kind);
	}

	[Fact]
	[Trait("Qualification", "Q44")]
	public void RemoteExecutionTryOperationsReturnCopiedDefaultsAndStableFailures()
	{
		UnavailableRemoteExecutionClient client = new();
		RemoteDllInjectionRequest injection = new(Path.GetFullPath("fixture.dll"));
		RemoteCallRequest call = new(_address, [1, 2, 3], TimeSpan.FromMilliseconds(1));

		Assert.False(client.TryInjectLibrary(injection, out CheatEngineFailure injectFailure,
			TestContext.Current.CancellationToken));
		Assert.Equal("RemoteExecution.InjectLibrary", injectFailure.Operation);

		Assert.False(client.TryInvoke(call, out RemoteCallResult result, out CheatEngineFailure callFailure,
			TestContext.Current.CancellationToken));
		Assert.Equal(default, result);
		Assert.Equal("RemoteExecution.Invoke", callFailure.Operation);
	}

	[Fact]
	[Trait("Qualification", "Q44")]
	public void RemoteExecutionThrowingOperationPreservesTheUnavailableFailure()
	{
		UnavailableRemoteExecutionClient client = new();
		RemoteDllInjectionRequest request = new(Path.GetFullPath("fixture.dll"));

		CheatEngineOperationException exception = Assert.Throws<CheatEngineOperationException>(() =>
		{
			client.InjectLibrary(request, TestContext.Current.CancellationToken);
		});

		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, exception.Failure.Kind);
	}

	[Fact]
	[Trait("Qualification", "Q44")]
	public void SpeedTryOperationsReturnDefaultsAndStableFailures()
	{
		UnavailableSpeedClient client = new();

		Assert.False(client.TryGetMultiplier(out SpeedMultiplier multiplier, out CheatEngineFailure getFailure,
			TestContext.Current.CancellationToken));
		Assert.Equal(default, multiplier);
		Assert.Equal("Speed.GetMultiplier", getFailure.Operation);

		Assert.False(client.TrySetMultiplier(new SpeedMultiplier(1), out CheatEngineFailure setFailure,
			TestContext.Current.CancellationToken));
		Assert.Equal("Speed.SetMultiplier", setFailure.Operation);
	}

	[Fact]
	[Trait("Qualification", "Q44")]
	public void SpeedThrowingOperationPreservesTheUnavailableFailure()
	{
		UnavailableSpeedClient client = new();

		CheatEngineOperationException exception = Assert.Throws<CheatEngineOperationException>(() =>
		{
			_ = client.GetMultiplier(TestContext.Current.CancellationToken);
		});

		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, exception.Failure.Kind);
	}

	[Fact]
	[Trait("Qualification", "Q44")]
	public void HashingKeepsMemoryAndFileFailuresAsSeparateOperations()
	{
		UnavailableHashingClient client = new();
		MemoryHashRequest memory = new(_address, 4);
		FileHashRequest file = new(Path.GetFullPath("fixture.bin"));

		Assert.False(client.TryHashMemory(memory, out HashDigest memoryDigest, out CheatEngineFailure memoryFailure,
			TestContext.Current.CancellationToken));
		Assert.Equal(default, memoryDigest);
		Assert.Equal("Hashing.HashMemory", memoryFailure.Operation);

		Assert.False(client.TryHashFile(file, out HashDigest fileDigest, out CheatEngineFailure fileFailure,
			TestContext.Current.CancellationToken));
		Assert.Equal(default, fileDigest);
		Assert.Equal("Hashing.HashFile", fileFailure.Operation);
	}

	[Fact]
	[Trait("Qualification", "Q44")]
	public void CancellationTakesPrecedenceOverTheCapabilityGate()
	{
		UnavailableHashingClient client = new();
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();

		bool succeeded = client.TryHashMemory(new MemoryHashRequest(_address, 1), out _, out CheatEngineFailure failure,
			cancellation.Token);

		Assert.False(succeeded);
		Assert.Equal(CheatEngineFailureKind.Cancelled, failure.Kind);
		Assert.Equal("Hashing.HashMemory", failure.Operation);
	}

	[Fact]
	[Trait("Qualification", "Q44")]
	public void DebuggerTryRegistrationReturnsNoLeaseAndTheLiveGateFailure()
	{
		UnavailableDebuggerClient client = new();

		bool succeeded = client.TryRegisterBreakpoint(new BreakpointRequest(_address),
			static _ => BreakpointDisposition.Continue,
			new EventStreamOptions(1), out IBreakpointLease? lease, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Null(lease);
		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, failure.Kind);
		Assert.Equal("Debugger.RegisterBreakpoint", failure.Operation);
	}

	[Fact]
	[Trait("Qualification", "Q44")]
	public void DebuggerThrowingRegistrationPreservesTheUnavailableFailure()
	{
		UnavailableDebuggerClient client = new();

		CheatEngineOperationException exception = Assert.Throws<CheatEngineOperationException>(() =>
		{
			_ = client.RegisterBreakpoint(new BreakpointRequest(_address), static _ => BreakpointDisposition.Continue,
				new EventStreamOptions(1), TestContext.Current.CancellationToken);
		});

		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, exception.Failure.Kind);
	}

	[Fact]
	[Trait("Qualification", "Q44")]
	public void DebuggerRegistrationRejectsDefaultStreamOptionsAndNullHandlerBeforeCapabilityGate()
	{
		UnavailableDebuggerClient client = new();
		BreakpointRequest request = new(_address);

		Assert.Throws<ArgumentOutOfRangeException>(() => client.TryRegisterBreakpoint(request,
			static _ => BreakpointDisposition.Continue, default, out _, out _, TestContext.Current.CancellationToken));
		Assert.Throws<ArgumentNullException>(() => client.TryRegisterBreakpoint(request, null!,
			new EventStreamOptions(1),
			out _, out _, TestContext.Current.CancellationToken));
	}

	[Fact]
	[Trait("Qualification", "Q44")]
	public void HotkeyTryRegistrationReturnsNoLeaseAndTheLiveGateFailure()
	{
		UnavailableHotkeyClient client = new();
		HotkeyRegistration registration = new("fixture", new HotkeyGesture(0x41));

		bool succeeded = client.TryRegister(registration, static _ =>
			{
			}, new EventStreamOptions(1),
			out IHotkeyLease? lease, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Null(lease);
		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, failure.Kind);
		Assert.Equal("Hotkeys.Register", failure.Operation);
	}

	[Fact]
	[Trait("Qualification", "Q44")]
	public void HotkeyThrowingRegistrationPreservesTheUnavailableFailure()
	{
		UnavailableHotkeyClient client = new();
		HotkeyRegistration registration = new("fixture", new HotkeyGesture(0x41));

		CheatEngineOperationException exception = Assert.Throws<CheatEngineOperationException>(() =>
		{
			_ = client.Register(registration, static _ =>
			{
			}, new EventStreamOptions(1), TestContext.Current.CancellationToken);
		});

		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, exception.Failure.Kind);
	}

	[Fact]
	[Trait("Qualification", "Q44")]
	public void HotkeyRegistrationRejectsDefaultStreamOptionsAndNullHandlerBeforeCapabilityGate()
	{
		UnavailableHotkeyClient client = new();
		HotkeyRegistration registration = new("fixture", new HotkeyGesture(0x41));

		Assert.Throws<ArgumentOutOfRangeException>(() => client.TryRegister(registration, static _ =>
			{
			}, default,
			out _, out _, TestContext.Current.CancellationToken));
		Assert.Throws<ArgumentNullException>(() => client.TryRegister(registration, null!, new EventStreamOptions(1),
			out _, out _, TestContext.Current.CancellationToken));
	}

	[Fact]
	[Trait("Qualification", "Q44")]
	public void TimerTryRegistrationReturnsNoLeaseAndTheLiveGateFailure()
	{
		UnavailableTimerClient client = new();

		bool succeeded = client.TryRegister(new TimerRequest(TimeSpan.FromMilliseconds(1)), static _ =>
			{
			},
			new EventStreamOptions(1), out ITimerLease? lease, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Null(lease);
		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, failure.Kind);
		Assert.Equal("Timers.Register", failure.Operation);
	}

	[Fact]
	[Trait("Qualification", "Q44")]
	public void TimerThrowingRegistrationPreservesTheUnavailableFailure()
	{
		UnavailableTimerClient client = new();

		CheatEngineOperationException exception = Assert.Throws<CheatEngineOperationException>(() =>
		{
			_ = client.Register(new TimerRequest(TimeSpan.FromMilliseconds(1)), static _ =>
				{
				},
				new EventStreamOptions(1), TestContext.Current.CancellationToken);
		});

		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, exception.Failure.Kind);
	}

	[Fact]
	[Trait("Qualification", "Q44")]
	public void TimerRegistrationRejectsDefaultStreamOptionsAndNullHandlerBeforeCapabilityGate()
	{
		UnavailableTimerClient client = new();
		TimerRequest request = new(TimeSpan.FromMilliseconds(1));

		Assert.Throws<ArgumentOutOfRangeException>(() => client.TryRegister(request, static _ =>
			{
			}, default,
			out _, out _, TestContext.Current.CancellationToken));
		Assert.Throws<ArgumentNullException>(() => client.TryRegister(request, null!, new EventStreamOptions(1),
			out _, out _, TestContext.Current.CancellationToken));
	}

	[Fact]
	[Trait("Qualification", "Q44")]
	public void DbvmObservesWithoutInitializingAndKeepsWatchRegistrationGated()
	{
		UnavailableDbvmClient client = new();

		Assert.False(client.TryGetStatus(out DbvmStatusSnapshot status, out CheatEngineFailure statusFailure,
			TestContext.Current.CancellationToken));
		Assert.Equal(default, status);
		Assert.Equal("Dbvm.GetStatus", statusFailure.Operation);

		Assert.False(client.TryInitialize(new DbvmInitializationRequest(), out DbvmStatusSnapshot initialized,
			out CheatEngineFailure initializeFailure, TestContext.Current.CancellationToken));
		Assert.Equal(default, initialized);
		Assert.Equal("Dbvm.Initialize", initializeFailure.Operation);

		Assert.False(client.TryRegisterWatch(new DbvmWatchRequest(_address, 1), static _ =>
			{
			}, new EventStreamOptions(1),
			out IDbvmWatchLease? lease, out CheatEngineFailure watchFailure, TestContext.Current.CancellationToken));
		Assert.Null(lease);
		Assert.Equal("Dbvm.RegisterWatch", watchFailure.Operation);
	}

	[Fact]
	[Trait("Qualification", "Q44")]
	public void DbvmThrowingObservationPreservesTheUnavailableFailure()
	{
		UnavailableDbvmClient client = new();

		CheatEngineOperationException exception = Assert.Throws<CheatEngineOperationException>(() =>
		{
			_ = client.GetStatus(TestContext.Current.CancellationToken);
		});

		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, exception.Failure.Kind);
	}

	[Fact]
	[Trait("Qualification", "Q44")]
	public void DbvmWatchRegistrationRejectsDefaultStreamOptionsAndNullHandlerBeforeCapabilityGate()
	{
		UnavailableDbvmClient client = new();
		DbvmWatchRequest request = new(_address, 1);

		Assert.Throws<ArgumentOutOfRangeException>(() => client.TryRegisterWatch(request, static _ =>
			{
			}, default,
			out _, out _, TestContext.Current.CancellationToken));
		Assert.Throws<ArgumentNullException>(() => client.TryRegisterWatch(request, null!, new EventStreamOptions(1),
			out _, out _, TestContext.Current.CancellationToken));
	}

	[Fact]
	[Trait("Qualification", "Q44")]
	public void DbvmThrowingWatchRegistrationPreservesTheUnavailableFailure()
	{
		UnavailableDbvmClient client = new();

		CheatEngineOperationException exception = Assert.Throws<CheatEngineOperationException>(() =>
		{
			_ = client.RegisterWatch(new DbvmWatchRequest(_address, 1), static _ =>
				{
				}, new EventStreamOptions(1),
				TestContext.Current.CancellationToken);
		});

		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, exception.Failure.Kind);
	}
}
