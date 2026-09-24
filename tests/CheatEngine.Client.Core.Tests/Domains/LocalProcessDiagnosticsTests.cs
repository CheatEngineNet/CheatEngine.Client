using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;

namespace CheatEngine.Client.Core.Tests.Domains;

public sealed class LocalProcessDiagnosticsTests
{
	[Fact]
	public void GetProcessesReturnsBoundedOrderedCopiedLocalMetadata()
	{
		FakeLocalProcessHost host = new(
		[
			new LocalProcessInfo(52, "alpha-worker", "C:\\fixtures\\alpha-worker.exe"),
			new LocalProcessInfo(43, "alpha-server", "C:\\fixtures\\alpha-server.exe"),
			new LocalProcessInfo(44, "beta", "C:\\fixtures\\beta.exe")
		]);
		LocalProcessDiagnostics diagnostics = new(host);

		ProcessEnumerationResult result = diagnostics.GetProcesses(
			new ProcessEnumerationRequest(1, "ALPHA"),
			TestContext.Current.CancellationToken);

		Assert.True(result.IsTruncated);
		ProcessInfoSnapshot snapshot = Assert.Single(result.Processes);
		Assert.Equal(new LocalProcessId(43), snapshot.Id);
		Assert.Equal("alpha-server", snapshot.Name);
		Assert.Equal("C:\\fixtures\\alpha-server.exe", snapshot.ExecutablePath);
	}

	[Fact]
	public void TryGetProcessesHonorsCancellationBeforeReadingTheLocalCatalog()
	{
		FakeLocalProcessHost host = new([]);
		LocalProcessDiagnostics diagnostics = new(host);
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();

		bool succeeded = diagnostics.TryGetProcesses(
			new ProcessEnumerationRequest(1),
			out ProcessEnumerationResult result,
			out CheatEngineFailure failure,
			cancellation.Token);

		Assert.False(succeeded);
		Assert.Equal(default, result);
		Assert.Equal(CheatEngineFailureKind.Cancelled, failure.Kind);
		Assert.Equal("LocalProcesses.GetProcesses", failure.Operation);
		Assert.Equal(0, host.GetLocalProcessesCalls);
	}

	[Fact]
	public void TryGetProcessesRejectsAnInvalidRequestBeforeReadingTheLocalCatalog()
	{
		FakeLocalProcessHost host = new([]);
		LocalProcessDiagnostics diagnostics = new(host);

		Assert.Throws<ArgumentOutOfRangeException>(() =>
			diagnostics.TryGetProcesses(default, out _, out _, TestContext.Current.CancellationToken));

		Assert.Equal(0, host.GetLocalProcessesCalls);
	}

	[Fact]
	public void TryGetProcessesMapsLocalCatalogFailuresWithoutClaimingTargetState()
	{
		FakeLocalProcessHost host = new([])
		{
			GetLocalProcessesException = new InvalidOperationException("fixture enumeration failed")
		};
		LocalProcessDiagnostics diagnostics = new(host);

		bool succeeded = diagnostics.TryGetProcesses(
			new ProcessEnumerationRequest(1),
			out ProcessEnumerationResult result,
			out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, result);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal("LocalProcesses.GetProcesses", failure.Operation);
		Assert.Equal(1, host.GetLocalProcessesCalls);
	}

	[Fact]
	public void CopiedLocalDiagnosticsRemainUsableAfterAClientActivationExpires()
	{
		FakeLocalProcessHost host = new(
		[
			new LocalProcessInfo(43, "fixture", "C:\\fixtures\\fixture.exe")
		]);
		LocalProcessDiagnostics diagnostics = new(host);
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		ProcessInfoSnapshot snapshot = Assert.Single(diagnostics.GetProcesses(
			new ProcessEnumerationRequest(1), TestContext.Current.CancellationToken).Processes);
		context.IsCurrent = false;

		Assert.Throws<CheatEngineActivationExpiredException>(() => lifetime.ThrowIfInactive("Test.Stale"));
		Assert.Equal(new LocalProcessId(43), snapshot.Id);
		Assert.Equal("fixture", snapshot.Name);
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
			init;
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
}
