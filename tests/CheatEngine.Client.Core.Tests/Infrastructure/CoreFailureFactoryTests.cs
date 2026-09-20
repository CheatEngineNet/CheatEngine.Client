using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Results;

namespace CheatEngine.Client.Core.Tests.Infrastructure;

public sealed class CoreFailureFactoryTests
{
	[Fact]
	public void LifecycleCreatesTheStableInvalidStateFailure()
	{
		CheatEngineFailure failure = CoreFailureFactory.Lifecycle("Client.DrainResources", "The client is stopping.");

		Assert.Equal(CheatEngineFailureKind.InvalidState, failure.Kind);
		Assert.Equal("Client.DrainResources", failure.Operation);
		Assert.Equal("The client is stopping.", failure.Message);
		Assert.Null(failure.Exception);
	}

	[Fact]
	public void FromExceptionPreservesDedicatedActivationExpiryClassification()
	{
		CheatEngineActivationExpiredException exception = new("Memory.Read", "The epoch changed.");

		CheatEngineFailure failure = CoreFailureFactory.FromException("Dispatcher.Invoke", exception);

		Assert.Equal(CheatEngineFailureKind.ActivationExpired, failure.Kind);
		Assert.Equal("Dispatcher.Invoke", failure.Operation);
		Assert.Equal(exception.Message, failure.Message);
		Assert.Same(exception, failure.Exception);
	}
}
