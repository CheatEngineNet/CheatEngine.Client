using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
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
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, selectionLifetime);
		ProcessSnapshot initial = client.GetCurrent(TestContext.Current.CancellationToken);
		host.OpenedProcessId = 0;

		bool succeeded = client.TryGetCurrent(
			out ProcessSnapshot snapshot,
			out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, snapshot);
		Assert.Equal(CheatEngineFailureKind.TargetNotAttached, failure.Kind);
		Assert.Equal(1, selectionLifetime.Epoch);
		Assert.Throws<CheatEngineClientLifecycleException>(() =>
			selectionLifetime.ThrowIfExpired(initial.SelectionEpoch, "Test.TargetLease"));

		CheatEngineOperationException exception = Assert.Throws<CheatEngineOperationException>(() =>
			client.GetCurrent(TestContext.Current.CancellationToken));
		Assert.Equal(CheatEngineFailureKind.TargetNotAttached, exception.Failure.Kind);
	}

	[Fact]
	public void AttachVerifiesThePidSelectedByCheatEngineBeforeReturningTheSnapshot()
	{
		FakeProcessHost host = FakeProcessHost.CreateSelected(42, CheatEngineArchitecture.X64);
		host.LocalProcesses[43] = new LocalProcessInfo(43, "fixture-b", "C:\\fixtures\\fixture-b.exe");
		host.SelectedAfterOpenOverride = 42;
		using TargetSelectionLifetime selectionLifetime = CreateSelectionLifetime();
		ProcessClient client = new(new InlineDispatcher(), host, selectionLifetime);

		bool succeeded = client.TryAttach(
			new TargetProcessId(43),
			out ProcessSnapshot snapshot,
			out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, snapshot);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal([43L], host.OpenProcessCalls);
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
			return OpenedProcessId;
		}

		public void OpenProcess(long processId)
		{
			OpenProcessCalls.Add(processId);
			OpenedProcessId = SelectedAfterOpenOverride ?? processId;
		}

		public bool TryGetLocalProcess(int processId, out LocalProcessInfo process)
		{
			return LocalProcesses.TryGetValue(processId, out process);
		}

		public IReadOnlyList<LocalProcessInfo> FindProcessesByExactName(string processName)
		{
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

			try
			{
				callback();
				failure = default;
				return true;
			}
			catch (Exception exception)
			{
				failure = new CheatEngineFailure(
					CheatEngineFailureKind.OperationRejected,
					"Dispatcher.Invoke",
					exception.Message,
					exception);
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
				failure = new CheatEngineFailure(
					CheatEngineFailureKind.Cancelled,
					"Dispatcher.Invoke",
					"Cancelled before dispatch.");
				return false;
			}

			try
			{
				result = callback();
				failure = default;
				return true;
			}
			catch (Exception exception)
			{
				result = default!;
				failure = new CheatEngineFailure(
					CheatEngineFailureKind.OperationRejected,
					"Dispatcher.Invoke",
					exception.Message,
					exception);
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
			if (TryInvoke(callback, out T result, out CheatEngineFailure failure, cancellationToken))
			{
				return result;
			}

			failure.Throw();
			return default!;
		}
	}
}
