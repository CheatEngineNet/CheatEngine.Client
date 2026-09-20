using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;

namespace CheatEngine.Client.Core.Tests.Domains;

public sealed class PatternScannerCoverageTests
{
	[Fact]
	public void TryScanRejectsAnUninitializedRequestBeforeAccessingThePluginDispatcher()
	{
		PatternScanner scanner = CreateScanner();

		bool succeeded = scanner.TryScan(default, out AobScanResult result, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, result);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal("Patterns.Scan", failure.Operation);
		Assert.Contains("normalized, non-empty pattern", failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ScanConvertsAnUninitializedRequestFailureToThePublicOperationException()
	{
		PatternScanner scanner = CreateScanner();

		CheatEngineOperationException exception = Assert.Throws<CheatEngineOperationException>(() =>
			scanner.Scan(default, TestContext.Current.CancellationToken));

		Assert.Equal(CheatEngineFailureKind.OperationRejected, exception.Failure.Kind);
		Assert.Equal("Patterns.Scan", exception.Failure.Operation);
		Assert.Contains("normalized, non-empty pattern", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ConstructorRejectsANullDispatcher()
	{
		ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() => new PatternScanner(null!));

		Assert.Equal("dispatcher", exception.ParamName);
	}

	private static PatternScanner CreateScanner()
	{
		return new PatternScanner(new SdkMainThreadDispatcher(InertCoreLifetime.Create()));
	}
}
