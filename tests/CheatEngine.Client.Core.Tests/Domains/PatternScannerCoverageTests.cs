using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Tests.TestSupport;

namespace CheatEngine.Client.Core.Tests.Domains;

public sealed class PatternScannerCoverageTests
{
	[Fact]
	public void TryScanThrowsForAnUninitializedRequestBeforeAccessingThePluginDispatcher()
	{
		PatternScanner scanner = CreateScanner();

		ArgumentException exception = Assert.Throws<ArgumentException>(() => scanner.TryScan(default, out _, out _,
			TestContext.Current.CancellationToken));

		Assert.Equal("request", exception.ParamName);
		Assert.Contains("normalized, non-empty pattern", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ScanThrowsTheArgumentExceptionOfAnUninitializedRequest()
	{
		PatternScanner scanner = CreateScanner();

		ArgumentException exception = Assert.Throws<ArgumentException>(() =>
			scanner.Scan(default, TestContext.Current.CancellationToken));

		Assert.Equal("request", exception.ParamName);
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
