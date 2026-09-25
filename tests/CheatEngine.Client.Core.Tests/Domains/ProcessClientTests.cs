using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Processes;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Tests.Domains;

public sealed class ProcessClientTests
{
	[Fact]
	public void RefreshKeepsTheSelectionEpochForTheSamePidAndArchitecture()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, host.Port, host.Port, selectionLifetime);

		ProcessSnapshot initial = client.GetCurrentProcess(TestContext.Current.CancellationToken);
		ProcessSnapshot refreshed = client.Refresh(TestContext.Current.CancellationToken);

		Assert.Equal(initial.Id, refreshed.Id);
		Assert.Equal(initial.Architecture, refreshed.Architecture);
		Assert.Equal(0, initial.SelectionEpoch);
		Assert.Equal(initial.SelectionEpoch, refreshed.SelectionEpoch);
	}

	[Fact]
	public void RefreshChangesPidAdvancesSelectionEpochAndDisposesTheOldTargetLease()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		host.LocalProcesses[43] = new LocalProcessInfo(43, "fixture-b", "C:\\fixtures\\fixture-b.exe");
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, host.Port, host.Port, selectionLifetime);
		ProcessSnapshot initial = client.GetCurrentProcess(TestContext.Current.CancellationToken);
		RecordingDisposable lease = new();
		selectionLifetime.Track(lease, initial.SelectionEpoch);
		host.OpenedProcessId = 43;

		ProcessSnapshot refreshed = client.Refresh(TestContext.Current.CancellationToken);

		Assert.Equal(new TargetProcessId(43), refreshed.Id);
		Assert.Equal(1, refreshed.SelectionEpoch);
		Assert.Equal(1, lease.DisposeCount);
		Assert.Throws<CheatEngineInvalidStateException>(() =>
			selectionLifetime.ThrowIfExpired(initial.SelectionEpoch, "Test.TargetLease"));
	}

	[Fact]
	public void RefreshChangesArchitectureForTheSamePidAndAdvancesSelectionEpoch()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X86);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, host.Port, host.Port, selectionLifetime);
		ProcessSnapshot initial = client.GetCurrentProcess(TestContext.Current.CancellationToken);
		host.TargetArchitecture = CheatEngineArchitecture.X64;

		ProcessSnapshot refreshed = client.Refresh(TestContext.Current.CancellationToken);

		Assert.Equal(new TargetProcessId(42), refreshed.Id);
		Assert.Equal(CheatEngineArchitecture.X64, refreshed.Architecture);
		Assert.Equal(1, refreshed.SelectionEpoch);
		Assert.NotEqual(initial.SelectionEpoch, refreshed.SelectionEpoch);
	}

	[Fact]
	public void TryGetCurrentProcessReportsTargetNotAttachedAndInvalidatesTheKnownSelectionWhenCeDetaches()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		using ControlledCoreLifetimeContext activationContext = new();
		using CoreLifetime activationLifetime = new(activationContext);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(
			new SdkMainThreadDispatcher(activationLifetime, new InlineMainThreadInvoker()),
			host,
			host.Port,
			host.Port,
			selectionLifetime);
		ProcessSnapshot initial = client.GetCurrentProcess(TestContext.Current.CancellationToken);
		host.OpenedProcessId = 0;

		bool succeeded = client.TryGetCurrentProcess(
			out ProcessSnapshot snapshot,
			out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, snapshot);
		Assert.Equal(CheatEngineFailureKind.TargetNotAttached, failure.Kind);
		Assert.Equal("Processes.GetCurrentProcess", failure.Operation);
		Assert.Equal(1, selectionLifetime.Epoch);
		Assert.Throws<CheatEngineInvalidStateException>(() =>
			selectionLifetime.ThrowIfExpired(initial.SelectionEpoch, "Test.TargetLease"));

		CheatEngineOperationException exception = Assert.Throws<CheatEngineOperationException>(() =>
			client.GetCurrentProcess(TestContext.Current.CancellationToken));
		Assert.Equal(CheatEngineFailureKind.TargetNotAttached, exception.Failure.Kind);
	}

	[Fact]
	public void TryRefreshRetainsTheCheatEngineSelectionWhenLocalMetadataDisappears()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		using ControlledCoreLifetimeContext activationContext = new();
		using CoreLifetime activationLifetime = new(activationContext);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(
			new SdkMainThreadDispatcher(activationLifetime, new InlineMainThreadInvoker()),
			host,
			host.Port,
			host.Port,
			selectionLifetime);
		ProcessSnapshot initial = client.GetCurrentProcess(TestContext.Current.CancellationToken);
		RecordingDisposable lease = new();
		selectionLifetime.Track(lease, initial.SelectionEpoch);
		host.LocalProcesses.Remove(initial.Id.Value);

		bool firstSucceeded = client.TryRefresh(
			out ProcessSnapshot firstSnapshot,
			out CheatEngineFailure firstFailure,
			TestContext.Current.CancellationToken);
		bool secondSucceeded = client.TryRefresh(
			out ProcessSnapshot secondSnapshot,
			out CheatEngineFailure secondFailure,
			TestContext.Current.CancellationToken);

		Assert.True(firstSucceeded);
		Assert.Equal(default, firstFailure);
		Assert.Equal(initial.Id, firstSnapshot.Id);
		Assert.Null(firstSnapshot.Name);
		Assert.Null(firstSnapshot.ExecutablePath);
		Assert.True(secondSucceeded);
		Assert.Equal(default, secondFailure);
		Assert.Equal(initial.Id, secondSnapshot.Id);
		Assert.Equal(initial.SelectionEpoch, secondSnapshot.SelectionEpoch);
		Assert.Equal(initial.SelectionEpoch, selectionLifetime.Epoch);
		Assert.Equal(0, lease.DisposeCount);
	}

	[Fact]
	public void TryGetCurrentProcessReportsInvalidHostResultForInconsistentLocalMetadata()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		host.LocalProcesses[42] = new LocalProcessInfo(41, "fixture", "C:\\fixtures\\fixture.exe");
		using ControlledCoreLifetimeContext activationContext = new();
		using CoreLifetime activationLifetime = new(activationContext);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(
			new SdkMainThreadDispatcher(activationLifetime, new InlineMainThreadInvoker()),
			host,
			host.Port,
			host.Port,
			selectionLifetime);

		bool succeeded = client.TryGetCurrentProcess(
			out ProcessSnapshot snapshot,
			out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, snapshot);
		Assert.Equal(CheatEngineFailureKind.InvalidHostResult, failure.Kind);
		Assert.Equal("Processes.GetCurrentProcess", failure.Operation);
		Assert.IsType<EngineMarshallingException>(failure.Exception);
		Assert.Equal(0, selectionLifetime.Epoch);
	}

	[Fact]
	public void TryGetCurrentProcessReturnsUnexpectedHostExceptionsAsClassifiedFailures()
	{
		// C-CORE-A's SdkBoundary rule (F15): a fault of a Client-internal host call never crosses a Try method.
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		ObjectDisposedException expected = new("fixture process host");
		host.Port.TargetFault = expected;
		using ControlledCoreLifetimeContext activationContext = new();
		using CoreLifetime activationLifetime = new(activationContext);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(
			new SdkMainThreadDispatcher(activationLifetime, new InlineMainThreadInvoker()),
			host,
			host.Port,
			host.Port,
			selectionLifetime);

		bool succeeded = client.TryGetCurrentProcess(out ProcessSnapshot snapshot, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, snapshot);
		Assert.Equal(CheatEngineFailureKind.InvalidState, failure.Kind);
		Assert.Equal("Processes.GetCurrentProcess", failure.Operation);
		Assert.Equal(CheatEngineHostEffect.Unknown, failure.HostEffect);
		Assert.Same(expected, failure.Exception);
		Assert.Equal(0, selectionLifetime.Epoch);
	}

	[Fact]
	public void TryAttachReturnsAFaultOfTheAttachCallWithAnUnknownHostEffect()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		LuaException expected = new("openProcess failed");
		host.Port.SelectFault = expected;
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, host.Port, host.Port, selectionLifetime);

		bool succeeded = client.TryAttach(new TargetProcessId(43), out ProcessSnapshot snapshot,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, snapshot);
		Assert.Equal(CheatEngineFailureKind.LuaError, failure.Kind);
		Assert.Equal("Processes.Attach", failure.Operation);
		Assert.Equal(CheatEngineHostEffect.Unknown, failure.HostEffect);
		Assert.Same(expected, failure.Exception);
	}

	[Fact]
	[Trait("Qualification", "Q32")]
	public void RefreshWithAChangedProcessWidthForTheSamePidAdvancesTheSelectionEpoch()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.Unknown);
		host.IsX86 = false;
		host.IsArm = false;
		host.Is64Bit = false;
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, host.Port, host.Port, selectionLifetime);
		ProcessSnapshot initial = client.GetCurrentProcess(TestContext.Current.CancellationToken);
		RecordingDisposable lease = new();
		selectionLifetime.Track(lease, initial.SelectionEpoch);
		host.Is64Bit = true;

		ProcessSnapshot refreshed = client.Refresh(TestContext.Current.CancellationToken);

		Assert.Equal(CheatEngineArchitecture.Unknown, initial.Architecture);
		Assert.Equal(PointerSize.Bit32, initial.Bitness);
		Assert.Equal(PointerSize.Bit64, refreshed.Bitness);
		Assert.Equal(initial.SelectionEpoch + 1, refreshed.SelectionEpoch);
		Assert.Equal(1, lease.DisposeCount);
	}

	[Fact]
	[Trait("Qualification", "Q32")]
	public void RefreshWithTransientlyUnknownIsaFactsKeepsTheSelectionEpochAndTheLastKnownFacts()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, host.Port, host.Port, selectionLifetime);
		ProcessSnapshot initial = client.GetCurrentProcess(TestContext.Current.CancellationToken);
		RecordingDisposable lease = new();
		selectionLifetime.Track(lease, initial.SelectionEpoch);
		// A raising target fact narrows the observation: the ISA is unknown for this read, the PID and bitness are not.
		host.Port.TargetStatus = TargetObservations.LuaFailure;

		ProcessSnapshot transient = client.Refresh(TestContext.Current.CancellationToken);
		host.Port.TargetStatus = ProcessOperationStatus.Success;
		ProcessSnapshot recovered = client.Refresh(TestContext.Current.CancellationToken);

		Assert.Equal(initial.SelectionEpoch, transient.SelectionEpoch);
		Assert.Equal(CheatEngineArchitecture.X64, transient.Architecture);
		Assert.Equal(PointerSize.Bit64, transient.Bitness);
		Assert.Equal(initial.SelectionEpoch, recovered.SelectionEpoch);
		Assert.Equal(CheatEngineArchitecture.X64, recovered.Architecture);
		Assert.Equal(0, lease.DisposeCount);
	}

	[Fact]
	[Trait("Qualification", "Q32")]
	public void RefreshAfterAPidChangeAdvancesTheEpochEvenWhenTheIsaIsUnknown()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		host.LocalProcesses[43] = new LocalProcessInfo(43, "fixture-b", "C:\\fixtures\\fixture-b.exe");
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, host.Port, host.Port, selectionLifetime);
		ProcessSnapshot initial = client.GetCurrentProcess(TestContext.Current.CancellationToken);
		host.OpenedProcessId = 43;
		host.IsX86 = null;

		ProcessSnapshot refreshed = client.Refresh(TestContext.Current.CancellationToken);

		Assert.Equal(new TargetProcessId(43), refreshed.Id);
		Assert.Equal(initial.SelectionEpoch + 1, refreshed.SelectionEpoch);
		Assert.Equal(CheatEngineArchitecture.Unknown, refreshed.Architecture);
		Assert.Equal(PointerSize.Bit64, refreshed.Bitness);
	}

	[Fact]
	[Trait("Qualification", "Q32")]
	public void CurrentSnapshotStoresTheObservedProcessWidthWhenTheIsaIsUnknown()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		host.IsX86 = false;
		host.IsArm = false;
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, host.Port, host.Port, selectionLifetime);

		ProcessSnapshot snapshot = client.GetCurrentProcess(TestContext.Current.CancellationToken);

		Assert.Equal(CheatEngineArchitecture.Unknown, snapshot.Architecture);
		Assert.Equal(PointerSize.Bit64, snapshot.Bitness);
		Assert.Equal([nameof(ITargetObservationPort.ObserveTargetArchitecture)], host.Port.TargetCalls);
	}

	[Fact]
	[Trait("Qualification", "Q32")]
	public void CurrentSnapshotReadsNoArchitectureFactWithoutASelectedProcess()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		host.OpenedProcessId = 0;
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, host.Port, host.Port, selectionLifetime);

		bool succeeded = client.TryGetCurrentProcess(out _, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(CheatEngineFailureKind.TargetNotAttached, failure.Kind);
		// The SDK reads no target fact without a selected process, and the Client re-reads nothing.
		Assert.Equal([nameof(ITargetObservationPort.ObserveTargetArchitecture)], host.Port.TargetCalls);
	}

	[Fact]
	public void TryGetCurrentProcessKeepsThePidAndTheBitnessWhenOneTargetFactRaises()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		host.Port.TargetStatus = TargetObservations.LuaFailure;
		using ControlledCoreLifetimeContext activationContext = new();
		using CoreLifetime activationLifetime = new(activationContext);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(
			new SdkMainThreadDispatcher(activationLifetime, new InlineMainThreadInvoker()),
			host,
			host.Port,
			host.Port,
			selectionLifetime);

		bool succeeded = client.TryGetCurrentProcess(out ProcessSnapshot snapshot, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.Equal(new TargetProcessId(42), snapshot.Id);
		Assert.Equal(CheatEngineArchitecture.Unknown, snapshot.Architecture);
		Assert.Equal(PointerSize.Bit64, snapshot.Bitness);
	}

	[Fact]
	[Trait("Qualification", "Q32")]
	public void CurrentSnapshotReportsATargetChangeWhenThePidChangesDuringObservation()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		host.Port.TargetStatus = ProcessOperationStatus.TargetChanged;
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, host.Port, host.Port, selectionLifetime);

		bool succeeded = client.TryGetCurrentProcess(out ProcessSnapshot snapshot, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, snapshot);
		Assert.Equal(CheatEngineFailureKind.TargetChanged, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Completed, failure.HostEffect);
		Assert.Equal(0, selectionLifetime.Epoch);
	}

	[Fact]
	[Trait("Qualification", "Q32")]
	public void CurrentSnapshotReportsAFailedClosingReadAsALuaErrorRatherThanAsATargetChange()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		host.Port.TargetStatus = TargetObservations.LuaFailure;
		host.Port.CurrentReads = [(ProcessOperationStatus.Success, 42), (TargetObservations.LuaFailure, 0)];
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, host.Port, host.Port, selectionLifetime);

		bool succeeded = client.TryGetCurrentProcess(out ProcessSnapshot snapshot, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, snapshot);
		Assert.Equal(CheatEngineFailureKind.LuaError, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Completed, failure.HostEffect);
		Assert.DoesNotContain("changed", failure.Message, StringComparison.Ordinal);
		Assert.Equal(0, selectionLifetime.Epoch);
	}

	[Fact]
	public void TryGetCurrentProcessHonorsCancellationBeforeProductionDispatchAdmission()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		using ControlledCoreLifetimeContext activationContext = new();
		using CoreLifetime activationLifetime = new(activationContext);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(
			new SdkMainThreadDispatcher(activationLifetime, new InlineMainThreadInvoker()),
			host,
			host.Port,
			host.Port,
			selectionLifetime);
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();

		bool succeeded = client.TryGetCurrentProcess(out ProcessSnapshot snapshot, out CheatEngineFailure failure,
			cancellation.Token);

		Assert.False(succeeded);
		Assert.Equal(default, snapshot);
		Assert.Equal(CheatEngineFailureKind.Cancelled, failure.Kind);
		Assert.Empty(host.Port.TargetCalls);
	}

	[Fact]
	public void InlineDispatcherPreservesCallbackExceptionIdentity()
	{
		InlineDispatcher dispatcher = new();
		InvalidOperationException expected = new("fixture callback");

		InvalidOperationException actual = Assert.Throws<InvalidOperationException>(() =>
			dispatcher.TryInvoke(() => throw expected, out _, TestContext.Current.CancellationToken));

		Assert.Same(expected, actual);
	}

	[Fact]
	public void AttachVerifiesThePidSelectedByCheatEngineBeforeReturningTheSnapshot()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		host.LocalProcesses[43] = new LocalProcessInfo(43, "fixture-b", "C:\\fixtures\\fixture-b.exe");
		host.SelectedAfterOpenOverride = 42;
		using ControlledCoreLifetimeContext activationContext = new();
		using CoreLifetime activationLifetime = new(activationContext);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(
			new SdkMainThreadDispatcher(activationLifetime, new InlineMainThreadInvoker()),
			host,
			host.Port,
			host.Port,
			selectionLifetime);

		bool succeeded = client.TryAttach(
			new TargetProcessId(43),
			out ProcessSnapshot snapshot,
			out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, snapshot);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal("Processes.Attach", failure.Operation);
		Assert.Equal([43], host.Port.SelectCalls);
	}

	[Fact]
	public void TryAttachReturnsTheSelectedCheatEngineTargetWhenLocalMetadataDisappearsAfterOpen()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		host.LocalProcesses[43] = new LocalProcessInfo(43, "fixture-b", "C:\\fixtures\\fixture-b.exe");
		host.AfterOpenProcess = static processHost => processHost.LocalProcesses.Remove(43);
		using ControlledCoreLifetimeContext activationContext = new();
		using CoreLifetime activationLifetime = new(activationContext);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(
			new SdkMainThreadDispatcher(activationLifetime, new InlineMainThreadInvoker()),
			host,
			host.Port,
			host.Port,
			selectionLifetime);
		ProcessSnapshot initial = client.GetCurrentProcess(TestContext.Current.CancellationToken);
		RecordingDisposable lease = new();
		selectionLifetime.Track(lease, initial.SelectionEpoch);

		bool succeeded = client.TryAttach(
			new TargetProcessId(43),
			out ProcessSnapshot snapshot,
			out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.Equal(new TargetProcessId(43), snapshot.Id);
		Assert.Null(snapshot.Name);
		Assert.Null(snapshot.ExecutablePath);
		Assert.Equal([43], host.Port.SelectCalls);
		Assert.Equal(1, selectionLifetime.Epoch);
		Assert.Equal(1, lease.DisposeCount);
	}

	[Fact]
	public void TryAttachSelectsTheRequestedPidAndAdvancesAnAlreadyObservedSelection()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		host.LocalProcesses[43] = new LocalProcessInfo(43, "fixture-b", "C:\\fixtures\\fixture-b.exe");
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, host.Port, host.Port, selectionLifetime);
		ProcessSnapshot initial = client.GetCurrentProcess(TestContext.Current.CancellationToken);
		RecordingDisposable lease = new();
		selectionLifetime.Track(lease, initial.SelectionEpoch);

		bool succeeded = client.TryAttach(
			new TargetProcessId(43),
			out ProcessSnapshot snapshot,
			out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.Equal(new TargetProcessId(43), snapshot.Id);
		Assert.Equal(1, snapshot.SelectionEpoch);
		Assert.Equal(1, lease.DisposeCount);
		Assert.Equal([43], host.Port.SelectCalls);
	}

	[Fact]
	public void TryAttachExactNameRejectsZeroAndMultipleCandidatesWithoutOpeningAnyProcess()
	{
		FakeProcessHost noMatchHost = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		using TargetSelectionLifetime noMatchLifetime = CreateSelectionLifetime();
		ProcessClient noMatchClient = new(new InlineDispatcher(), noMatchHost, noMatchHost.Port, noMatchHost.Port, noMatchLifetime);

		bool noMatchSucceeded = noMatchClient.TryAttachExactName(
			"absent.exe",
			out ProcessSnapshot noMatchSnapshot,
			out CheatEngineFailure noMatchFailure,
			TestContext.Current.CancellationToken);

		Assert.False(noMatchSucceeded);
		Assert.Equal(default, noMatchSnapshot);
		Assert.Equal(CheatEngineFailureKind.NotFound, noMatchFailure.Kind);
		Assert.Empty(noMatchHost.Port.SelectCalls);

		FakeProcessHost ambiguousHost = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		ambiguousHost.NameMatches["fixture"] =
		[
			new LocalProcessInfo(43, "fixture", "C:\\fixtures\\one.exe"),
			new LocalProcessInfo(44, "fixture", "C:\\fixtures\\two.exe")
		];
		using TargetSelectionLifetime ambiguousLifetime = CreateSelectionLifetime();
		ProcessClient ambiguousClient = new(new InlineDispatcher(), ambiguousHost, ambiguousHost.Port, ambiguousHost.Port, ambiguousLifetime);

		bool ambiguousSucceeded = ambiguousClient.TryAttachExactName(
			"fixture.exe",
			out ProcessSnapshot ambiguousSnapshot,
			out CheatEngineFailure ambiguousFailure,
			TestContext.Current.CancellationToken);

		Assert.False(ambiguousSucceeded);
		Assert.Equal(default, ambiguousSnapshot);
		Assert.Equal(CheatEngineFailureKind.AmbiguousMatch, ambiguousFailure.Kind);
		Assert.Empty(ambiguousHost.Port.SelectCalls);
	}

	[Fact]
	public void TryAttachExactNameRejectsAStaleActivationBeforeLocalProcessDiscovery()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		using ControlledCoreLifetimeContext context = new()
		{
			IsCurrent = false
		};
		using CoreLifetime lifetime = new(context);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, host.Port, host.Port, selectionLifetime, lifetime.ThrowIfInactive);

		CheatEngineActivationExpiredException exception = Assert.Throws<CheatEngineActivationExpiredException>(() =>
			client.TryAttachExactName("fixture.exe", out _, out _, TestContext.Current.CancellationToken));

		Assert.Equal("Processes.AttachExactName", exception.Failure.Operation);
		Assert.Equal(0, host.FindProcessesByExactNameCalls);
		Assert.Empty(host.Port.SelectCalls);
	}

	[Fact]
	public void TryAttachExactNameHonorsCancellationBeforeLocalProcessDiscovery()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, host.Port, host.Port, selectionLifetime, lifetime.ThrowIfInactive);
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();

		bool succeeded = client.TryAttachExactName("fixture.exe", out ProcessSnapshot snapshot,
			out CheatEngineFailure failure, cancellation.Token);

		Assert.False(succeeded);
		Assert.Equal(default, snapshot);
		Assert.Equal(CheatEngineFailureKind.Cancelled, failure.Kind);
		Assert.Equal(0, host.FindProcessesByExactNameCalls);
	}

	[Fact]
	public void TryAttachExactNameMapsLocalCatalogFailuresBeforeChangingTheCheatEngineSelection()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		host.FindProcessesByExactNameException = new InvalidOperationException("fixture discovery failed");
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, host.Port, host.Port, selectionLifetime);

		bool succeeded = client.TryAttachExactName("fixture.exe", out ProcessSnapshot snapshot,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, snapshot);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal("Processes.AttachExactName", failure.Operation);
		Assert.Equal(1, host.FindProcessesByExactNameCalls);
		Assert.Empty(host.Port.SelectCalls);
	}

	[Fact]
	public void AttachExactNameUsesTheSingleExactCandidateAndReturnsCopiedMetadata()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		host.LocalProcesses[43] = new LocalProcessInfo(43, "fixture", "C:\\fixtures\\fixture.exe");
		host.NameMatches["fixture"] = [host.LocalProcesses[43]];
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, host.Port, host.Port, selectionLifetime);

		ProcessSnapshot snapshot = client.AttachExactName("fixture.exe", TestContext.Current.CancellationToken);

		Assert.Equal(new TargetProcessId(43), snapshot.Id);
		Assert.Equal("fixture", snapshot.Name);
		Assert.Equal("C:\\fixtures\\fixture.exe", snapshot.ExecutablePath);
		Assert.Equal([43], host.Port.SelectCalls);
	}

	[Theory]
	[InlineData("C:\\fixtures\\fixture.exe")]
	[InlineData(".exe")]
	[InlineData(" ")]
	public void ExactNameAttachmentRejectsPathsAndInvalidNamesBeforeProcessDiscovery(string name)
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, host.Port, host.Port, selectionLifetime);

		Assert.Throws<ArgumentException>(() =>
			client.TryAttachExactName(name, out _, out _, TestContext.Current.CancellationToken));
		Assert.Empty(host.Port.SelectCalls);
	}

	[Theory]
	[Trait("Qualification", "Q32")]
	[InlineData(ProcessOperationStatusKind.GlobalUnavailable, CheatEngineFailureKind.CapabilityUnavailable)]
	[InlineData(ProcessOperationStatusKind.TargetChanged, CheatEngineFailureKind.TargetChanged)]
	[InlineData(ProcessOperationStatusKind.FileAsProcessTarget, CheatEngineFailureKind.TargetIdentityUnavailable)]
	[InlineData(ProcessOperationStatusKind.SelectionNotConfirmed, CheatEngineFailureKind.OperationRejected)]
	[InlineData(ProcessOperationStatusKind.Unknown, CheatEngineFailureKind.IndeterminateHostResult)]
	public void TryGetCurrentProcessReportsEveryStatusThatEstablishesNoTargetWithItsOwnKind(
		ProcessOperationStatusKind status, CheatEngineFailureKind expected)
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		host.Port.TargetStatus = TargetObservations.Status(status);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, host.Port, host.Port, selectionLifetime);

		bool succeeded = client.TryGetCurrentProcess(out ProcessSnapshot snapshot, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, snapshot);
		Assert.Equal(expected, failure.Kind);
		Assert.Equal("Processes.GetCurrentProcess", failure.Operation);
		Assert.Equal(CheatEngineHostEffect.Completed, failure.HostEffect);
		Assert.Null(failure.Exception);
	}

	[Fact]
	[Trait("Qualification", "Q32")]
	public void AFileOpenedAsAProcessInvalidatesTheKnownProcessSelection()
	{
		// A file opened as a process has no process identity: the earlier selection no longer holds (audit A12-07).
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, host.Port, host.Port, selectionLifetime);
		ProcessSnapshot initial = client.GetCurrentProcess(TestContext.Current.CancellationToken);
		RecordingDisposable lease = new();
		selectionLifetime.Track(lease, initial.SelectionEpoch);
		host.Port.TargetStatus = ProcessOperationStatus.FileAsProcessTarget;

		bool succeeded = client.TryRefresh(out _, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(CheatEngineFailureKind.TargetIdentityUnavailable, failure.Kind);
		Assert.Equal(1, selectionLifetime.Epoch);
		Assert.Equal(1, lease.DisposeCount);
	}

	[Theory]
	[Trait("Qualification", "Q30.a")]
	[InlineData(ProcessOperationStatusKind.SelectionNotConfirmed, CheatEngineFailureKind.OperationRejected)]
	[InlineData(ProcessOperationStatusKind.TargetNotAttached, CheatEngineFailureKind.TargetNotAttached)]
	[InlineData(ProcessOperationStatusKind.FileAsProcessTarget, CheatEngineFailureKind.TargetIdentityUnavailable)]
	[InlineData(ProcessOperationStatusKind.TargetChanged, CheatEngineFailureKind.TargetChanged)]
	[InlineData(ProcessOperationStatusKind.GlobalUnavailable, CheatEngineFailureKind.CapabilityUnavailable)]
	[InlineData(ProcessOperationStatusKind.ProtectedLuaFailure, CheatEngineFailureKind.LuaError)]
	[InlineData(ProcessOperationStatusKind.InvalidResult, CheatEngineFailureKind.InvalidHostResult)]
	[InlineData(ProcessOperationStatusKind.Unknown, CheatEngineFailureKind.IndeterminateHostResult)]
	public void TryAttachReportsEveryRefusedSelectionWithItsKindAndAnUnknownHostEffect(
		ProcessOperationStatusKind status, CheatEngineFailureKind expected)
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		host.Port.SelectStatus = TargetObservations.Status(status);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, host.Port, host.Port, selectionLifetime);

		bool succeeded = client.TryAttach(new TargetProcessId(43), out ProcessSnapshot snapshot,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, snapshot);
		Assert.Equal(expected, failure.Kind);
		Assert.Equal("Processes.Attach", failure.Operation);
		Assert.Equal(CheatEngineHostEffect.Unknown, failure.HostEffect);
		Assert.Equal([43], host.Port.SelectCalls);
	}

	[Fact]
	[Trait("Qualification", "Q30.a")]
	public void ARefusedAttachStillMovesTheSelectionEpochToWhatCheatEngineNowSelects()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		host.SelectedAfterOpenOverride = 44;
		host.LocalProcesses[44] = new LocalProcessInfo(44, "fixture-c", "C:\\fixtures\\fixture-c.exe");
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, host.Port, host.Port, selectionLifetime);
		ProcessSnapshot initial = client.GetCurrentProcess(TestContext.Current.CancellationToken);
		RecordingDisposable lease = new();
		selectionLifetime.Track(lease, initial.SelectionEpoch);

		bool succeeded = client.TryAttach(new TargetProcessId(43), out _, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal(1, selectionLifetime.Epoch);
		Assert.Equal(1, lease.DisposeCount);
		Assert.Equal(new TargetProcessId(44), client.GetCurrentProcess(TestContext.Current.CancellationToken).Id);
	}

	[Fact]
	[Trait("Qualification", "Q30.b")]
	public void TheSelectionEpochAdvancesWhenTheSamePidDenotesAnotherIncarnation()
	{
		// The PID alone can be reused: the SDK's creation-time observation tells the two processes apart.
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		host.Port.Incarnation = TargetObservations.Incarnation(42, 1_000);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, host.Port, host.Port, selectionLifetime);
		ProcessSnapshot initial = client.GetCurrentProcess(TestContext.Current.CancellationToken);
		RecordingDisposable lease = new();
		selectionLifetime.Track(lease, initial.SelectionEpoch);
		host.Port.Incarnation = TargetObservations.Incarnation(42, 2_000);

		ProcessSnapshot refreshed = client.Refresh(TestContext.Current.CancellationToken);

		Assert.Equal(new TargetProcessId(42), refreshed.Id);
		Assert.Equal(initial.SelectionEpoch + 1, refreshed.SelectionEpoch);
		Assert.Equal(1, lease.DisposeCount);
		Assert.Equal(1, host.Port.Count(nameof(IRuntimeObservationPort.ObserveSelection)));
		Assert.Equal(1, host.Port.Count(nameof(IRuntimeObservationPort.ValidateSelection)));
	}

	[Fact]
	[Trait("Qualification", "Q30.b")]
	public void AnUnchangedIncarnationKeepsTheSelectionEpoch()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		host.Port.Incarnation = TargetObservations.Incarnation(42, 1_000);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, host.Port, host.Port, selectionLifetime);

		ProcessSnapshot initial = client.GetCurrentProcess(TestContext.Current.CancellationToken);
		ProcessSnapshot first = client.Refresh(TestContext.Current.CancellationToken);
		ProcessSnapshot second = client.Refresh(TestContext.Current.CancellationToken);

		Assert.Equal(0, initial.SelectionEpoch);
		Assert.Equal(0, first.SelectionEpoch);
		Assert.Equal(0, second.SelectionEpoch);
		Assert.Equal(2, host.Port.Count(nameof(IRuntimeObservationPort.ValidateSelection)));
	}

	[Theory]
	[Trait("Qualification", "Q30.b")]
	[InlineData(TargetSelectionObservationStatus.CurrentTargetUnqualified)]
	[InlineData(TargetSelectionObservationStatus.LuaFailure)]
	[InlineData(TargetSelectionObservationStatus.GlobalUnavailable)]
	[InlineData(TargetSelectionObservationStatus.InvalidResult)]
	[InlineData(TargetSelectionObservationStatus.CurrentTargetBackendUnknown)]
	public void AnUnavailableIdentityReadKeepsTheSelectionEpoch(TargetSelectionObservationStatus status)
	{
		// No comparable incarnation is no evidence of a change: leases stay valid.
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		host.Port.Incarnation = TargetObservations.Incarnation(42, 1_000);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, host.Port, host.Port, selectionLifetime);
		ProcessSnapshot initial = client.GetCurrentProcess(TestContext.Current.CancellationToken);
		host.Port.SelectionStatus = status;

		bool succeeded = client.TryRefresh(out ProcessSnapshot refreshed, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);
		host.Port.SelectionStatus = null;
		host.Port.Incarnation = TargetObservations.Incarnation(42, 2_000);
		ProcessSnapshot reused = client.Refresh(TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.Equal(initial.SelectionEpoch, refreshed.SelectionEpoch);
		// The last known incarnation was kept, so a later reuse is still detected.
		Assert.Equal(initial.SelectionEpoch + 1, reused.SelectionEpoch);
	}

	[Theory]
	[Trait("Qualification", "Q30.a")]
	[InlineData(TargetSelectionObservationStatus.NoTargetSelected, false)]
	[InlineData(TargetSelectionObservationStatus.CurrentTargetFileAsProcess, false)]
	[InlineData(TargetSelectionObservationStatus.CurrentTargetRemoteBackend, false)]
	[InlineData(TargetSelectionObservationStatus.NoTargetSelected, true)]
	[InlineData(TargetSelectionObservationStatus.CurrentTargetRemoteBackend, true)]
	public void ASelectionThatMovesBetweenTheFactsAndTheIdentityReadIsATargetChange(
		TargetSelectionObservationStatus status, bool withKnownIncarnation)
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		host.Port.Incarnation = TargetObservations.Incarnation(42, 1_000);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, host.Port, host.Port, selectionLifetime);
		if (withKnownIncarnation)
		{
			_ = client.GetCurrentProcess(TestContext.Current.CancellationToken);
		}

		host.Port.SelectionStatus = status;

		bool succeeded = client.TryRefresh(out ProcessSnapshot snapshot, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, snapshot);
		Assert.Equal(CheatEngineFailureKind.TargetChanged, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Completed, failure.HostEffect);
		Assert.Equal(0, selectionLifetime.Epoch);
	}

	[Theory]
	[Trait("Qualification", "Q32")]
	[InlineData(TargetBackend.CEServer)]
	[InlineData(TargetBackend.Unknown)]
	public void ATargetThatIsNotALocalProcessGetsNoLocalMetadataAndNoIncarnationRead(TargetBackend backend)
	{
		// A local PID and creation time do not describe a PID served by CEServer or of an unknown backend (A12-05).
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		host.Backend = backend;
		host.Port.Incarnation = TargetObservations.Incarnation(42, 1_000);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, host.Port, host.Port, selectionLifetime);

		ProcessSnapshot snapshot = client.GetCurrentProcess(TestContext.Current.CancellationToken);

		Assert.Equal(new TargetProcessId(42), snapshot.Id);
		Assert.Equal(backend, snapshot.Backend);
		Assert.Null(snapshot.Name);
		Assert.Null(snapshot.ExecutablePath);
		Assert.Null(snapshot.StartTimeUtc);
		Assert.Equal(0, host.Port.Count(nameof(IRuntimeObservationPort.ObserveSelection)));
		Assert.Equal(0, host.Port.Count(nameof(IRuntimeObservationPort.ValidateSelection)));
	}

	[Fact]
	[Trait("Qualification", "Q32")]
	public void ALocalSnapshotCarriesItsBackendBitnessConfiguredSizeAndIncarnationStartTime()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X86);
		long startedAtUtcTicks = new DateTime(2026, 9, 24, 10, 30, 0, DateTimeKind.Utc).Ticks;
		host.Port.Incarnation = TargetObservations.Incarnation(42, startedAtUtcTicks);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, host.Port, host.Port, selectionLifetime);

		ProcessSnapshot snapshot = client.GetCurrentProcess(TestContext.Current.CancellationToken);

		Assert.Equal(TargetBackend.LocalProcess, snapshot.Backend);
		Assert.Equal(CheatEngineArchitecture.X86, snapshot.Architecture);
		Assert.Equal(PointerSize.Bit32, snapshot.Bitness);
		Assert.Equal(4, snapshot.ConfiguredPointerSizeBytes);
		Assert.False(snapshot.ConfiguredPointerSizeDiffersFromBitness);
		Assert.Equal(new DateTimeOffset(startedAtUtcTicks, TimeSpan.Zero), snapshot.StartTimeUtc);
		Assert.Equal("fixture", snapshot.Name);
	}

	[Fact]
	[Trait("Qualification", "Q31")]
	public void TheSnapshotReportsAConfiguredPointerSizeThatDiffersFromTheBitnessWithoutUsingIt()
	{
		// Spike C3 D3: setPointerSize(4) on an x64 target leaves targetIs64Bit true and readPointer 8 bytes wide.
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		host.Port.Target = TargetObservations.Create(configuredPointerSizeBytes: 4);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, host.Port, host.Port, selectionLifetime);

		ProcessSnapshot snapshot = client.GetCurrentProcess(TestContext.Current.CancellationToken);

		Assert.Equal(PointerSize.Bit64, snapshot.Bitness);
		Assert.Equal(4, snapshot.ConfiguredPointerSizeBytes);
		Assert.Equal(PointerSize.Bit32, snapshot.ConfiguredPointerSize);
		Assert.True(snapshot.ConfiguredPointerSizeDiffersFromBitness);
	}

	[Fact]
	[Trait("Qualification", "Q32")]
	public void ABackendChangeAtTheSamePidAdvancesTheSelectionEpoch()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, host.Port, host.Port, selectionLifetime);
		ProcessSnapshot initial = client.GetCurrentProcess(TestContext.Current.CancellationToken);
		host.Backend = TargetBackend.CEServer;

		ProcessSnapshot refreshed = client.Refresh(TestContext.Current.CancellationToken);

		Assert.Equal(initial.SelectionEpoch + 1, refreshed.SelectionEpoch);
		Assert.Null(refreshed.Name);
	}

	private static TargetSelectionLifetime CreateSelectionLifetime()
	{
		return new TargetSelectionLifetime(static _ =>
		{
		});
	}

	private sealed class RecordingDisposable : IDisposable
	{
		internal int DisposeCount
		{
			get;
			private set;
		}

		public void Dispose()
		{
			DisposeCount++;
		}
	}

	/// <summary>
	///     The local catalog and attach call of a process host, paired with an observation port whose selected target
	///     follows <see cref="OpenedProcessId" /> and the ISA facts.
	/// </summary>
	private sealed class FakeProcessHost : IProcessHost
	{
		private TargetBackend _backend = TargetBackend.LocalProcess;
		private bool _is64Bit;
		private bool? _isArm;
		private bool? _isX86;
		private long _openedProcessId;

		internal FakeRuntimeObservationPort Port
		{
			get;
		} = new();

		internal Dictionary<int, LocalProcessInfo> LocalProcesses
		{
			get;
		} = [];

		internal Dictionary<string, IReadOnlyList<LocalProcessInfo>> NameMatches
		{
			get;
		} =
			new(StringComparer.OrdinalIgnoreCase);

		internal int FindProcessesByExactNameCalls
		{
			get;
			private set;
		}

		internal Exception? FindProcessesByExactNameException
		{
			get;
			set;
		}

		internal Action<FakeProcessHost>? AfterOpenProcess
		{
			get;
			set;
		}

		/// <summary>Gets or sets Cheat Engine's selected PID; zero means that no target is selected.</summary>
		internal long OpenedProcessId
		{
			get => _openedProcessId;
			set
			{
				_openedProcessId = value;
				Synchronize();
			}
		}

		internal long? SelectedAfterOpenOverride
		{
			get;
			set;
		}

		/// <summary>Gets or sets how Cheat Engine reaches the selected target (a local process by default).</summary>
		internal TargetBackend Backend
		{
			get => _backend;
			set
			{
				_backend = value;
				Synchronize();
			}
		}

		/// <summary>Sets the ISA-family and 64-bit facts that Cheat Engine reports for the architecture.</summary>
		internal CheatEngineArchitecture TargetArchitecture
		{
			set
			{
				_isX86 = value is CheatEngineArchitecture.X86 or CheatEngineArchitecture.X64;
				_isArm = value is CheatEngineArchitecture.Arm32 or CheatEngineArchitecture.Arm64;
				_is64Bit = value is CheatEngineArchitecture.X64 or CheatEngineArchitecture.Arm64;
				Synchronize();
			}
		}

		internal bool Is64Bit
		{
			get => _is64Bit;
			set
			{
				_is64Bit = value;
				Synchronize();
			}
		}

		/// <summary>Gets or sets <c>targetIsX86</c>; <see langword="null" /> means that the global is absent.</summary>
		internal bool? IsX86
		{
			get => _isX86;
			set
			{
				_isX86 = value;
				Synchronize();
			}
		}

		/// <summary>Gets or sets <c>targetIsArm</c>; <see langword="null" /> means that the global is absent.</summary>
		internal bool? IsArm
		{
			get => _isArm;
			set
			{
				_isArm = value;
				Synchronize();
			}
		}

		/// <summary>What Cheat Engine selects when the Client asks for a PID (the fake port's attach call).</summary>
		private void Select(int processId)
		{
			OpenedProcessId = SelectedAfterOpenOverride ?? processId;
			AfterOpenProcess?.Invoke(this);
		}

		public bool TryGetLocalProcess(int processId, out LocalProcessInfo process)
		{
			return LocalProcesses.TryGetValue(processId, out process);
		}

		public IReadOnlyList<LocalProcessInfo> GetLocalProcesses()
		{
			return [.. LocalProcesses.Values];
		}

		public IReadOnlyList<LocalProcessInfo> FindProcessesByExactName(string processName)
		{
			FindProcessesByExactNameCalls++;
			if (FindProcessesByExactNameException is { } exception)
			{
				throw exception;
			}

			return NameMatches.TryGetValue(processName, out IReadOnlyList<LocalProcessInfo>? matches) ? matches : [];
		}

		internal static FakeProcessHost CreateSelected(int processId, CheatEngineArchitecture architecture)
		{
			FakeProcessHost host = new()
			{
				OpenedProcessId = processId,
				TargetArchitecture = architecture
			};
			host.Port.OnSelect = host.Select;
			host.LocalProcesses[processId] = new LocalProcessInfo(
				processId,
				"fixture",
				"C:\\fixtures\\fixture.exe");
			return host;
		}

		private void Synchronize()
		{
			Port.TargetStatus = _openedProcessId == 0
				? ProcessOperationStatus.TargetNotAttached
				: ProcessOperationStatus.Success;
			Port.Target = TargetObservations.Create((int) Math.Max(_openedProcessId, 1), _is64Bit, _isX86, _isArm,
				backend: Backend);
		}
	}

	private sealed class InlineDispatcher : ICheatEngineDispatcher
	{
		public bool IsMainThread => true;

		public bool TryInvoke(Action callback, out CheatEngineFailure failure,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(callback);
			if (cancellationToken.IsCancellationRequested)
			{
				failure = new CheatEngineFailure(
					CheatEngineFailureKind.Cancelled,
					"Dispatcher.Invoke",
					"Cancelled before dispatch.");
				return false;
			}

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
				failure = new CheatEngineFailure(
					CheatEngineFailureKind.Cancelled,
					"Dispatcher.Invoke",
					"Cancelled before dispatch.");
				return false;
			}

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
	}
}
