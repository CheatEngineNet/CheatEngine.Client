using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Core.Tests.Domains;

public sealed class ProcessClientTests
{
	[Fact]
	public void RefreshKeepsTheSelectionEpochForTheSamePidAndArchitecture()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, selectionLifetime);

		ProcessSnapshot initial = client.GetCurrent(TestContext.Current.CancellationToken);
		ProcessSnapshot refreshed = client.Refresh(TestContext.Current.CancellationToken);

		Assert.Equal(initial.Id, refreshed.Id);
		Assert.Equal(initial.TargetArchitecture, refreshed.TargetArchitecture);
		Assert.Equal(0, initial.SelectionEpoch);
		Assert.Equal(initial.SelectionEpoch, refreshed.SelectionEpoch);
	}

	[Fact]
	public void RefreshChangesPidAdvancesSelectionEpochAndDisposesTheOldTargetLease()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		host.LocalProcesses[43] = new LocalProcessInfo(43, "fixture-b", "C:\\fixtures\\fixture-b.exe");
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, selectionLifetime);
		ProcessSnapshot initial = client.GetCurrent(TestContext.Current.CancellationToken);
		RecordingDisposable lease = new();
		selectionLifetime.Track(lease, initial.SelectionEpoch);
		host.OpenedProcessId = 43;

		ProcessSnapshot refreshed = client.Refresh(TestContext.Current.CancellationToken);

		Assert.Equal(new TargetProcessId(43), refreshed.Id);
		Assert.Equal(1, refreshed.SelectionEpoch);
		Assert.Equal(1, lease.DisposeCount);
		Assert.Throws<CheatEngineClientLifecycleException>(() =>
			selectionLifetime.ThrowIfExpired(initial.SelectionEpoch, "Test.TargetLease"));
	}

	[Fact]
	public void RefreshChangesArchitectureForTheSamePidAndAdvancesSelectionEpoch()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X86);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, selectionLifetime);
		ProcessSnapshot initial = client.GetCurrent(TestContext.Current.CancellationToken);
		host.TargetArchitecture = CheatEngineArchitecture.X64;

		ProcessSnapshot refreshed = client.Refresh(TestContext.Current.CancellationToken);

		Assert.Equal(new TargetProcessId(42), refreshed.Id);
		Assert.Equal(CheatEngineArchitecture.X64, refreshed.TargetArchitecture);
		Assert.Equal(1, refreshed.SelectionEpoch);
		Assert.NotEqual(initial.SelectionEpoch, refreshed.SelectionEpoch);
	}

	[Fact]
	public void TryGetCurrentReportsTargetNotAttachedAndInvalidatesTheKnownSelectionWhenCeDetaches()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		using ControlledCoreLifetimeContext activationContext = new();
		using CoreLifetime activationLifetime = new(activationContext);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(
			new SdkMainThreadDispatcher(activationLifetime, new InlineMainThreadInvoker()),
			host,
			selectionLifetime);
		ProcessSnapshot initial = client.GetCurrent(TestContext.Current.CancellationToken);
		host.OpenedProcessId = 0;

		bool succeeded = client.TryGetCurrent(
			out ProcessSnapshot snapshot,
			out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, snapshot);
		Assert.Equal(CheatEngineFailureKind.TargetNotAttached, failure.Kind);
		Assert.Equal("Processes.GetCurrent", failure.Operation);
		Assert.Equal(1, selectionLifetime.Epoch);
		Assert.Throws<CheatEngineClientLifecycleException>(() =>
			selectionLifetime.ThrowIfExpired(initial.SelectionEpoch, "Test.TargetLease"));

		CheatEngineOperationException exception = Assert.Throws<CheatEngineOperationException>(() =>
			client.GetCurrent(TestContext.Current.CancellationToken));
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
			selectionLifetime);
		ProcessSnapshot initial = client.GetCurrent(TestContext.Current.CancellationToken);
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
	public void TryGetCurrentReportsInvalidHostResultForInconsistentLocalMetadata()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		host.LocalProcesses[42] = new LocalProcessInfo(41, "fixture", "C:\\fixtures\\fixture.exe");
		using ControlledCoreLifetimeContext activationContext = new();
		using CoreLifetime activationLifetime = new(activationContext);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(
			new SdkMainThreadDispatcher(activationLifetime, new InlineMainThreadInvoker()),
			host,
			selectionLifetime);

		bool succeeded = client.TryGetCurrent(
			out ProcessSnapshot snapshot,
			out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, snapshot);
		Assert.Equal(CheatEngineFailureKind.InvalidHostResult, failure.Kind);
		Assert.Equal("Processes.GetCurrent", failure.Operation);
		Assert.IsType<EngineMarshallingException>(failure.Exception);
		Assert.Equal(0, selectionLifetime.Epoch);
	}

	[Fact]
	public void TryGetCurrentRethrowsUnexpectedHostExceptions()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		ObjectDisposedException expected = new("fixture process host");
		host.GetOpenedProcessIdException = expected;
		using ControlledCoreLifetimeContext activationContext = new();
		using CoreLifetime activationLifetime = new(activationContext);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(
			new SdkMainThreadDispatcher(activationLifetime, new InlineMainThreadInvoker()),
			host,
			selectionLifetime);

		ObjectDisposedException actual = Assert.Throws<ObjectDisposedException>(() =>
			client.TryGetCurrent(out _, out _, TestContext.Current.CancellationToken));

		Assert.Same(expected, actual);
	}

	[Fact]
	public void TryGetCurrentHonorsCancellationBeforeProductionDispatchAdmission()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		using ControlledCoreLifetimeContext activationContext = new();
		using CoreLifetime activationLifetime = new(activationContext);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(
			new SdkMainThreadDispatcher(activationLifetime, new InlineMainThreadInvoker()),
			host,
			selectionLifetime);
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();

		bool succeeded = client.TryGetCurrent(out ProcessSnapshot snapshot, out CheatEngineFailure failure,
			cancellation.Token);

		Assert.False(succeeded);
		Assert.Equal(default, snapshot);
		Assert.Equal(CheatEngineFailureKind.Cancelled, failure.Kind);
		Assert.Equal(0, host.GetOpenedProcessIdCalls);
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
		Assert.Equal([43L], host.OpenProcessCalls);
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
			selectionLifetime);
		ProcessSnapshot initial = client.GetCurrent(TestContext.Current.CancellationToken);
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
		Assert.Equal([43L], host.OpenProcessCalls);
		Assert.Equal(1, selectionLifetime.Epoch);
		Assert.Equal(1, lease.DisposeCount);
	}

	[Fact]
	public void TryAttachSelectsTheRequestedPidAndAdvancesAnAlreadyObservedSelection()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		host.LocalProcesses[43] = new LocalProcessInfo(43, "fixture-b", "C:\\fixtures\\fixture-b.exe");
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, selectionLifetime);
		ProcessSnapshot initial = client.GetCurrent(TestContext.Current.CancellationToken);
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
		Assert.Equal([43L], host.OpenProcessCalls);
	}

	[Fact]
	public void TryAttachExactNameRejectsZeroAndMultipleCandidatesWithoutOpeningAnyProcess()
	{
		FakeProcessHost noMatchHost = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		using TargetSelectionLifetime noMatchLifetime = CreateSelectionLifetime();
		ProcessClient noMatchClient = new(new InlineDispatcher(), noMatchHost, noMatchLifetime);

		bool noMatchSucceeded = noMatchClient.TryAttachExactName(
			"absent.exe",
			out ProcessSnapshot noMatchSnapshot,
			out CheatEngineFailure noMatchFailure,
			TestContext.Current.CancellationToken);

		Assert.False(noMatchSucceeded);
		Assert.Equal(default, noMatchSnapshot);
		Assert.Equal(CheatEngineFailureKind.NotFound, noMatchFailure.Kind);
		Assert.Empty(noMatchHost.OpenProcessCalls);

		FakeProcessHost ambiguousHost = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		ambiguousHost.NameMatches["fixture"] =
		[
			new LocalProcessInfo(43, "fixture", "C:\\fixtures\\one.exe"),
			new LocalProcessInfo(44, "fixture", "C:\\fixtures\\two.exe")
		];
		using TargetSelectionLifetime ambiguousLifetime = CreateSelectionLifetime();
		ProcessClient ambiguousClient = new(new InlineDispatcher(), ambiguousHost, ambiguousLifetime);

		bool ambiguousSucceeded = ambiguousClient.TryAttachExactName(
			"fixture.exe",
			out ProcessSnapshot ambiguousSnapshot,
			out CheatEngineFailure ambiguousFailure,
			TestContext.Current.CancellationToken);

		Assert.False(ambiguousSucceeded);
		Assert.Equal(default, ambiguousSnapshot);
		Assert.Equal(CheatEngineFailureKind.AmbiguousMatch, ambiguousFailure.Kind);
		Assert.Empty(ambiguousHost.OpenProcessCalls);
	}

	[Fact]
	public void TryAttachExactNameRejectsAStaleActivationBeforeLocalProcessDiscovery()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		using ControlledCoreLifetimeContext context = new() { IsCurrent = false };
		using CoreLifetime lifetime = new(context);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, selectionLifetime, lifetime.ThrowIfInactive);

		CheatEngineActivationExpiredException exception = Assert.Throws<CheatEngineActivationExpiredException>(() =>
			client.TryAttachExactName("fixture.exe", out _, out _, TestContext.Current.CancellationToken));

		Assert.Equal("Processes.AttachExactName", exception.Failure.Operation);
		Assert.Equal(0, host.FindProcessesByExactNameCalls);
		Assert.Empty(host.OpenProcessCalls);
	}

	[Fact]
	public void TryAttachExactNameHonorsCancellationBeforeLocalProcessDiscovery()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, selectionLifetime, lifetime.ThrowIfInactive);
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
		ProcessClient client = new(new InlineDispatcher(), host, selectionLifetime);

		bool succeeded = client.TryAttachExactName("fixture.exe", out ProcessSnapshot snapshot,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, snapshot);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal("Processes.AttachExactName", failure.Operation);
		Assert.Equal(1, host.FindProcessesByExactNameCalls);
		Assert.Empty(host.OpenProcessCalls);
	}

	[Fact]
	public void AttachExactNameUsesTheSingleExactCandidateAndReturnsCopiedMetadata()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		host.LocalProcesses[43] = new LocalProcessInfo(43, "fixture", "C:\\fixtures\\fixture.exe");
		host.NameMatches["fixture"] = [host.LocalProcesses[43]];
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, selectionLifetime);

		ProcessSnapshot snapshot = client.AttachExactName("fixture.exe", TestContext.Current.CancellationToken);

		Assert.Equal(new TargetProcessId(43), snapshot.Id);
		Assert.Equal("fixture", snapshot.Name);
		Assert.Equal("C:\\fixtures\\fixture.exe", snapshot.ExecutablePath);
		Assert.Equal([43L], host.OpenProcessCalls);
	}

	[Theory]
	[InlineData("C:\\fixtures\\fixture.exe")]
	[InlineData(".exe")]
	[InlineData(" ")]
	public void ExactNameAttachmentRejectsPathsAndInvalidNamesBeforeProcessDiscovery(string name)
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, selectionLifetime);

		Assert.Throws<ArgumentException>(() =>
			client.TryAttachExactName(name, out _, out _, TestContext.Current.CancellationToken));
		Assert.Empty(host.OpenProcessCalls);
	}

	[Fact]
	public void TryAttachForegroundIsCapabilityGatedWithoutChangingTheObservedSelection()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, selectionLifetime);
		ProcessSnapshot initial = client.GetCurrent(TestContext.Current.CancellationToken);

		bool succeeded = client.TryAttachForeground(
			out ProcessSnapshot snapshot,
			out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, snapshot);
		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, failure.Kind);
		Assert.Equal(42, host.OpenedProcessId);
		Assert.Empty(host.OpenProcessCalls);
		Assert.Equal(initial.SelectionEpoch, selectionLifetime.Epoch);
	}

	[Fact]
	public void AttachForegroundThrowsTheStableCapabilityUnavailableFailure()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, selectionLifetime);

		CheatEngineOperationException exception = Assert.Throws<CheatEngineOperationException>(() =>
			client.AttachForeground(TestContext.Current.CancellationToken));

		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, exception.Failure.Kind);
		Assert.Equal("Processes.AttachForeground", exception.Failure.Operation);
		Assert.Empty(host.OpenProcessCalls);
	}

	[Fact]
	public void TryCreateIsCapabilityGatedWithoutStartingOrSelectingAnyProcess()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, selectionLifetime);
		ProcessSnapshot initial = client.GetCurrent(TestContext.Current.CancellationToken);

		bool succeeded = client.TryCreate(
			new ProcessStartRequest("C:\\fixtures\\target.exe"),
			out ProcessSnapshot snapshot,
			out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, snapshot);
		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, failure.Kind);
		Assert.Equal(42, host.OpenedProcessId);
		Assert.Empty(host.OpenProcessCalls);
		Assert.Equal(initial.SelectionEpoch, selectionLifetime.Epoch);
	}

	[Fact]
	public void TryCreateRejectsTheDefaultRequestBeforeHostAdmission()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, selectionLifetime);

		Assert.Throws<ArgumentException>(() => client.TryCreate(
			default,
			out _,
			out _,
			TestContext.Current.CancellationToken));

		Assert.Empty(host.OpenProcessCalls);
	}

	[Fact]
	public void TryPauseIsCapabilityGatedWithoutChangingTheObservedSelection()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, selectionLifetime);
		ProcessSnapshot initial = client.GetCurrent(TestContext.Current.CancellationToken);

		bool succeeded = client.TryPause(
			out ProcessSnapshot snapshot,
			out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, snapshot);
		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, failure.Kind);
		Assert.Equal(42, host.OpenedProcessId);
		Assert.Empty(host.OpenProcessCalls);
		Assert.Equal(initial.SelectionEpoch, selectionLifetime.Epoch);
	}

	[Fact]
	public void TryResumeExecutionIsCapabilityGatedWithoutChangingTheObservedSelection()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, selectionLifetime);
		ProcessSnapshot initial = client.GetCurrent(TestContext.Current.CancellationToken);

		bool succeeded = client.TryResumeExecution(
			out ProcessSnapshot snapshot,
			out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, snapshot);
		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, failure.Kind);
		Assert.Equal(42, host.OpenedProcessId);
		Assert.Empty(host.OpenProcessCalls);
		Assert.Equal(initial.SelectionEpoch, selectionLifetime.Epoch);
	}

	[Fact]
	public void TryGetPauseStateIsCapabilityGatedWithoutChangingTheObservedSelection()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, selectionLifetime);
		ProcessSnapshot initial = client.GetCurrent(TestContext.Current.CancellationToken);

		bool succeeded = client.TryGetPauseState(
			out ProcessPauseSnapshot snapshot,
			out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, snapshot);
		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, failure.Kind);
		Assert.Equal(42, host.OpenedProcessId);
		Assert.Empty(host.OpenProcessCalls);
		Assert.Equal(initial.SelectionEpoch, selectionLifetime.Epoch);
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

	private sealed class FakeProcessHost : IProcessHost
	{
		internal Dictionary<int, LocalProcessInfo> LocalProcesses
		{
			get;
		} = [];

		internal Dictionary<string, IReadOnlyList<LocalProcessInfo>> NameMatches
		{
			get;
		} =
			new(StringComparer.OrdinalIgnoreCase);

		internal List<long> OpenProcessCalls
		{
			get;
		} = [];

		internal int GetLocalProcessesCalls
		{
			get;
			private set;
		}

		internal int GetOpenedProcessIdCalls
		{
			get;
			private set;
		}

		internal int FindProcessesByExactNameCalls
		{
			get;
			private set;
		}

		internal Exception? GetLocalProcessesException
		{
			get;
			set;
		}

		internal Exception? GetOpenedProcessIdException
		{
			get;
			set;
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

		internal long OpenedProcessId
		{
			get;
			set;
		}

		internal long? SelectedAfterOpenOverride
		{
			get;
			set;
		}

		internal CheatEngineArchitecture TargetArchitecture
		{
			get;
			set;
		}

		public long GetOpenedProcessId()
		{
			GetOpenedProcessIdCalls++;
			if (GetOpenedProcessIdException is { } exception)
			{
				throw exception;
			}

			return OpenedProcessId;
		}

		public void OpenProcess(long processId)
		{
			OpenProcessCalls.Add(processId);
			OpenedProcessId = SelectedAfterOpenOverride ?? processId;
			AfterOpenProcess?.Invoke(this);
		}

		public bool TryGetLocalProcess(int processId, out LocalProcessInfo process)
		{
			return LocalProcesses.TryGetValue(processId, out process);
		}

		public IReadOnlyList<LocalProcessInfo> GetLocalProcesses()
		{
			GetLocalProcessesCalls++;
			if (GetLocalProcessesException is { } exception)
			{
				throw exception;
			}

			return LocalProcesses.Values.ToArray();
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

		public CheatEngineArchitecture GetTargetArchitecture()
		{
			return TargetArchitecture;
		}

		internal static FakeProcessHost CreateSelected(int processId, CheatEngineArchitecture architecture)
		{
			FakeProcessHost host = new() { OpenedProcessId = processId, TargetArchitecture = architecture };
			host.LocalProcesses[processId] = new LocalProcessInfo(
				processId,
				"fixture",
				"C:\\fixtures\\fixture.exe");
			return host;
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
	}
}
