using CheatEngine.Client.Results;

namespace CheatEngine.Client.Tests.Results;

public sealed class CheatEngineFailureTests
{
	/// <summary>Preserves the classified diagnostic fields and their originating exception.</summary>
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

	/// <summary>Rejects diagnostics that cannot name an operation or provide a human-readable message.</summary>
	[Fact]
	public void ConstructorRejectsMissingOperationOrMessage()
	{
		Assert.Throws<ArgumentException>(() => new CheatEngineFailure(
			CheatEngineFailureKind.Unknown, " ", "message"));
		Assert.Throws<ArgumentException>(() => new CheatEngineFailure(
			CheatEngineFailureKind.Unknown, "Operation", " "));
	}

	/// <summary>Maps a general expected failure to an operation exception retaining its source failure.</summary>
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

	/// <summary>Maps activation expiration to its dedicated exception without losing its cause.</summary>
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

	/// <summary>Maps an invalid lifecycle state to its dedicated exception while retaining all diagnostics.</summary>
	[Fact]
	public void ThrowPreservesTheDedicatedLifecycleExceptionForInvalidState()
	{
		InvalidOperationException innerException = new("provider is stopping");
		CheatEngineFailure failure = new(
			CheatEngineFailureKind.InvalidState,
			"Client.Track",
			"The provider is stopping.",
			innerException);

		CheatEngineClientLifecycleException exception =
			Assert.Throws<CheatEngineClientLifecycleException>(failure.Throw);

		Assert.Equal(failure, exception.Failure);
		Assert.Equal(failure.Message, exception.Message);
		Assert.Same(innerException, exception.InnerException);
	}

	/// <summary>Assigns each concrete lifecycle exception the stable failure kind it represents.</summary>
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
