using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;

namespace CheatEngine.Client.Core.Tests.Domains;

/// <summary>
///     <see cref="IProcessClient.TryGetLocalProcesses" /> is an offline diagnostic of the local operating-system catalog:
///     it never reaches Cheat Engine, needs no current activation, and reports a cancellation as NotStarted.
/// </summary>
public sealed class ProcessClientLocalProcessesTests
{
	[Fact]
	public void GetLocalProcessesReturnsBoundedOrderedCopiedLocalMetadata()
	{
		(ProcessClient client, FakeLocalProcessHost host, FakeRuntimeObservationPort port) = Create(
		[
			new LocalProcessInfo(52, "alpha-worker", "C:\\fixtures\\alpha-worker.exe"),
			new LocalProcessInfo(43, "alpha-server", "C:\\fixtures\\alpha-server.exe"),
			new LocalProcessInfo(44, "beta", "C:\\fixtures\\beta.exe")
		]);

		ProcessEnumerationResult result = client.GetLocalProcesses(
			new ProcessEnumerationRequest(1, "ALPHA"),
			TestContext.Current.CancellationToken);

		Assert.True(result.IsTruncated);
		ProcessInfoSnapshot snapshot = Assert.Single(result.Processes);
		Assert.Equal(new LocalProcessId(43), snapshot.Id);
		Assert.Equal("alpha-server", snapshot.Name);
		Assert.Equal("C:\\fixtures\\alpha-server.exe", snapshot.ExecutablePath);
		Assert.Equal(1, host.GetLocalProcessesCalls);
		Assert.Empty(port.Calls);
		Assert.Empty(port.TargetCalls);
	}

	[Fact]
	public void TryGetLocalProcessesReportsACancellationAsNotStartedBeforeReadingTheLocalCatalog()
	{
		(ProcessClient client, FakeLocalProcessHost host, _) = Create([]);
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();

		bool succeeded = client.TryGetLocalProcesses(
			new ProcessEnumerationRequest(1),
			out ProcessEnumerationResult result,
			out CheatEngineFailure failure,
			cancellation.Token);

		Assert.False(succeeded);
		Assert.Equal(default, result);
		Assert.Equal(CheatEngineFailureKind.Cancelled, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal("Processes.GetLocalProcesses", failure.Operation);
		Assert.Equal(0, host.GetLocalProcessesCalls);
		Assert.IsType<CheatEngineOperationCanceledException>(Assert.ThrowsAny<OperationCanceledException>(() =>
			client.GetLocalProcesses(new ProcessEnumerationRequest(1), cancellation.Token)));
	}

	[Fact]
	public void TryGetLocalProcessesRejectsAnInvalidRequestBeforeReadingTheLocalCatalog()
	{
		(ProcessClient client, FakeLocalProcessHost host, _) = Create([]);

		Assert.Throws<ArgumentOutOfRangeException>(() =>
			client.TryGetLocalProcesses(default, out _, out _, TestContext.Current.CancellationToken));

		Assert.Equal(0, host.GetLocalProcessesCalls);
	}

	[Fact]
	public void TryGetLocalProcessesMapsLocalCatalogFailuresWithoutClaimingTargetState()
	{
		(ProcessClient client, FakeLocalProcessHost host, _) = Create([]);
		host.GetLocalProcessesException = new InvalidOperationException("fixture enumeration failed");

		bool succeeded = client.TryGetLocalProcesses(
			new ProcessEnumerationRequest(1),
			out ProcessEnumerationResult result,
			out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, result);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal("Processes.GetLocalProcesses", failure.Operation);
		Assert.Equal(1, host.GetLocalProcessesCalls);
	}

	[Fact]
	public void LocalProcessesRemainReadableAfterTheClientActivationExpires()
	{
		FakeLocalProcessHost host = new([new LocalProcessInfo(43, "fixture", "C:\\fixtures\\fixture.exe")]);
		FakeRuntimeObservationPort port = new();
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		ProcessClient client = new(new RefusingDispatcher(), host, port, port, lifetime);
		context.IsCurrent = false;

		ProcessInfoSnapshot snapshot = Assert.Single(client.GetLocalProcesses(
			new ProcessEnumerationRequest(1), TestContext.Current.CancellationToken).Processes);

		Assert.Throws<CheatEngineActivationExpiredException>(() => lifetime.ThrowIfInactive("Test.Stale"));
		Assert.Equal(new LocalProcessId(43), snapshot.Id);
		Assert.Equal("fixture", snapshot.Name);
		Assert.Empty(port.TargetCalls);
	}

	private static (ProcessClient Client, FakeLocalProcessHost Host, FakeRuntimeObservationPort Port) Create(
		IReadOnlyList<LocalProcessInfo> processes)
	{
		FakeLocalProcessHost host = new(processes);
		FakeRuntimeObservationPort port = new();
		ProcessClient client = new(new RefusingDispatcher(), host, port, port,
			new TargetSelectionLifetime(static _ =>
			{
			}));
		return (client, host, port);
	}

	private sealed class FakeLocalProcessHost(IReadOnlyList<LocalProcessInfo> processes) : IProcessHost
	{
		internal int GetLocalProcessesCalls
		{
			get;
			private set;
		}

		internal Exception? GetLocalProcessesException
		{
			get;
			set;
		}

		public bool TryGetLocalProcess(int processId, out LocalProcessInfo process)
		{
			process = default;
			return false;
		}

		public IReadOnlyList<LocalProcessInfo> GetLocalProcesses()
		{
			GetLocalProcessesCalls++;
			if (GetLocalProcessesException is { } exception)
			{
				throw exception;
			}

			return processes;
		}

		public IReadOnlyList<LocalProcessInfo> FindProcessesByExactName(string processName)
		{
			return [];
		}
	}

	/// <summary>A dispatcher that fails the test when used: the local catalog never dispatches to Cheat Engine.</summary>
	private sealed class RefusingDispatcher : ICheatEngineDispatcher
	{
		public bool IsMainThread => true;

		public bool TryInvoke(Action callback, out CheatEngineFailure failure,
			CancellationToken cancellationToken = default)
		{
			throw new InvalidOperationException("The local process catalog must not dispatch to Cheat Engine.");
		}

		public bool TryInvoke<T>(Func<T> callback, out T result, out CheatEngineFailure failure,
			CancellationToken cancellationToken = default)
		{
			throw new InvalidOperationException("The local process catalog must not dispatch to Cheat Engine.");
		}

		public void Invoke(Action callback, CancellationToken cancellationToken = default)
		{
			throw new InvalidOperationException("The local process catalog must not dispatch to Cheat Engine.");
		}

		public T Invoke<T>(Func<T> callback, CancellationToken cancellationToken = default)
		{
			throw new InvalidOperationException("The local process catalog must not dispatch to Cheat Engine.");
		}
	}
}
