using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Results;

namespace CheatEngine.Client.Core.Tests.Domains;

public sealed class UnavailableValueScannerExceptionCoverageTests
{
	[Fact]
	public void CreateSessionThrowsTheCancellationFailureWhenCancellationPrecedesTheCapabilityGate()
	{
		UnavailableValueScanner scanner = new();
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();

		CheatEngineOperationCanceledException exception = Assert.Throws<CheatEngineOperationCanceledException>(() =>
		{
			_ = scanner.CreateSession(cancellation.Token);
		});

		Assert.Equal(cancellation.Token, exception.CancellationToken);

		Assert.Equal(CheatEngineFailureKind.Cancelled, exception.Failure.Kind);
		Assert.Equal("Scans.CreateSession", exception.Failure.Operation);
		Assert.Equal("The operation was cancelled before Cheat Engine work began.", exception.Failure.Message);
	}
}
