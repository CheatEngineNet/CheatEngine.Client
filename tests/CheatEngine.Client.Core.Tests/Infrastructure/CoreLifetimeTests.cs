using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Results;

namespace CheatEngine.Client.Core.Tests.Infrastructure;

public sealed class CoreLifetimeTests
{
	[Fact]
	public void CaptureRejectsClientConstructionOutsideAnEnabledPluginEpoch()
	{
		CheatEngineClientLifecycleException exception =
			Assert.Throws<CheatEngineClientLifecycleException>(CoreLifetime.Capture);

		Assert.Equal(CheatEngineFailureKind.InvalidState, exception.Failure.Kind);
		Assert.Equal("Client.Activate", exception.Failure.Operation);
		Assert.Contains("has not enabled a plugin context", exception.Failure.Message, StringComparison.Ordinal);
	}
}
