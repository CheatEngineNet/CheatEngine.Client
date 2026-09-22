using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;

namespace CheatEngine.Client.Core.Tests.Domains;

public sealed class UnavailableValueScannerTests
{
	[Fact]
	public void TryCreateSessionReportsCapabilityUnavailableBeforeTheLiveOwnershipGate()
	{
		UnavailableValueScanner scanner = new();

		bool succeeded = scanner.TryCreateSession(
			out IValueScanSession? session, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Null(session);
		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, failure.Kind);
		Assert.Equal((string?) "Scans.CreateSession", (string?) failure.Operation);
		Assert.Contains("no public ownership factory", failure.Message, StringComparison.Ordinal);
		Assert.Contains("cannot return CEObject", failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void TryCreateSessionReportsCancellationWithoutAttemptingHostWork()
	{
		UnavailableValueScanner scanner = new();
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();

		bool succeeded = scanner.TryCreateSession(out IValueScanSession? session, out CheatEngineFailure failure,
			cancellation.Token);

		Assert.False(succeeded);
		Assert.Null(session);
		Assert.Equal(CheatEngineFailureKind.Cancelled, failure.Kind);
	}

	[Fact]
	public void TryCreateSessionRejectsAStaleActivationBeforeCapabilityOrCancellation()
	{
		using ControlledCoreLifetimeContext context = new()
		{
			IsCurrent = false
		};
		using CoreLifetime lifetime = new(context);
		UnavailableValueScanner scanner = new(lifetime);
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();

		CheatEngineActivationExpiredException exception = Assert.Throws<CheatEngineActivationExpiredException>(() =>
			scanner.TryCreateSession(out _, out _, cancellation.Token));

		Assert.Equal("Scans.CreateSession", exception.Failure.Operation);
	}

	[Fact]
	public void CreateSessionThrowsTheClassifiedUnavailableFailure()
	{
		UnavailableValueScanner scanner = new();

		CheatEngineOperationException exception = Assert.Throws<CheatEngineOperationException>(() =>
		{
			_ = scanner.CreateSession(TestContext.Current.CancellationToken);
		});

		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, exception.Failure.Kind);
	}
}
