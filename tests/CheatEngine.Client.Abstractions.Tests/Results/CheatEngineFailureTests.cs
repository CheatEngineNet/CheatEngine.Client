using CheatEngine.Client.Results;

namespace CheatEngine.Client.Tests.Results;

public sealed class CheatEngineFailureTests
{
	[Fact]
	public void ConstructorPreservesClassifiedDiagnosticAndInnerException()
	{
		InvalidOperationException innerException = new("sdk failure");

		CheatEngineFailure failure = new(
			CheatEngineFailureKind.MemoryReadFailed,
			"Memory.Read",
			"The target rejected the read.",
			innerException);

		Assert.Equal(CheatEngineFailureKind.MemoryReadFailed, failure.Kind);
		Assert.Equal("Memory.Read", failure.Operation);
		Assert.Equal("The target rejected the read.", failure.Message);
		Assert.Same(innerException, failure.Exception);
	}

	[Fact]
	public void ConstructorRejectsMissingOperationOrMessage()
	{
		Assert.Throws<ArgumentException>(() => new CheatEngineFailure(
			CheatEngineFailureKind.Unknown, " ", "message"));
		Assert.Throws<ArgumentException>(() => new CheatEngineFailure(
			CheatEngineFailureKind.Unknown, "Operation", " "));
	}

	[Fact]
	public void ThrowCreatesOperationExceptionThatRetainsFailure()
	{
		InvalidOperationException innerException = new("lua failure");
		CheatEngineFailure failure = new(
			CheatEngineFailureKind.LuaError,
			"Lua.Execute",
			"Lua returned an error.",
			innerException);

		CheatEngineOperationException exception = Assert.Throws<CheatEngineOperationException>(failure.Throw);

		Assert.Equal(failure, exception.Failure);
		Assert.Equal(failure.Message, exception.Message);
		Assert.Same(innerException, exception.InnerException);
	}

	[Fact]
	public void ThrowPreservesTheDedicatedActivationExpiredException()
	{
		InvalidOperationException innerException = new("epoch changed");
		CheatEngineFailure failure = new(
			CheatEngineFailureKind.ActivationExpired,
			"Memory.Read",
			"The client epoch is stale.",
			innerException);

		CheatEngineActivationExpiredException exception =
			Assert.Throws<CheatEngineActivationExpiredException>(failure.Throw);

		Assert.Equal(failure, exception.Failure);
		Assert.Same(innerException, exception.InnerException);
	}

	[Fact]
	public void LifecycleExceptionsClassifyTheirSpecificLifecycleFailures()
	{
		CheatEngineActivationExpiredException expired = new("Table.Read", "The epoch changed.");
		CheatEngineClientLifecycleException stopping = new("Client.Track", "The provider is stopping.");

		Assert.Equal(CheatEngineFailureKind.ActivationExpired, expired.Failure.Kind);
		Assert.Equal("Table.Read", expired.Failure.Operation);
		Assert.Equal(CheatEngineFailureKind.InvalidState, stopping.Failure.Kind);
		Assert.Equal("Client.Track", stopping.Failure.Operation);
	}
}
