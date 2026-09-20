using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Scanning.Aob;

namespace CheatEngine.Client.Core.Tests.Domains;

public sealed class PatternScannerTests
{
	[Fact]
	public void TryValidateRequestRejectsTheDefaultStructBeforeAnyDispatcherOrSdkOperation()
	{
		bool valid = PatternScanner.TryValidateRequest(default, out CheatEngineFailure failure);

		Assert.False(valid);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal("Patterns.Scan", failure.Operation);
	}

	[Fact]
	public void TryValidateRequestAcceptsAConstructedBoundedRequest()
	{
		AobScanRequest request = new(new AobPattern("90"), AobScanOptions.Default, 1);

		bool valid = PatternScanner.TryValidateRequest(request, out CheatEngineFailure failure);

		Assert.True(valid);
		Assert.Equal(default, failure);
	}
}
