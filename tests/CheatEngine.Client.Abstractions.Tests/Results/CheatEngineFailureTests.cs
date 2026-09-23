using CheatEngine.Client.Results;

namespace CheatEngine.Client.Abstractions.Tests.Results;

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

	/// <summary>Keeps the historical constructor conservative and round-trips an explicit host effect.</summary>
	[Fact]
	public void HostEffectDefaultsToUnknownAndRoundTripsThroughTheNewConstructor()
	{
		InvalidOperationException innerException = new("release failed");

		CheatEngineFailure legacy = new(CheatEngineFailureKind.LuaError, "Lua.Execute", "Lua failed.");
		CheatEngineFailure explicitEffect = new(CheatEngineFailureKind.InvalidState, "Patterns.Scan",
			"The release was not confirmed.", innerException, CheatEngineHostEffect.CleanupUnconfirmed);

		Assert.Equal(CheatEngineHostEffect.Unknown, default(CheatEngineFailure).HostEffect);
		Assert.Equal(CheatEngineHostEffect.Unknown, legacy.HostEffect);
		Assert.Equal(CheatEngineHostEffect.CleanupUnconfirmed, explicitEffect.HostEffect);
		Assert.Equal(CheatEngineFailureKind.InvalidState, explicitEffect.Kind);
		Assert.Same(innerException, explicitEffect.Exception);
		Assert.NotEqual(explicitEffect, new CheatEngineFailure(explicitEffect.Kind, explicitEffect.Operation,
			explicitEffect.Message, innerException, CheatEngineHostEffect.Completed));
	}

	/// <summary>Rejects a host effect outside the documented vocabulary instead of storing an unclassifiable value.</summary>
	[Theory]
	[InlineData(-1)]
	[InlineData(5)]
	[InlineData(int.MaxValue)]
	public void ConstructorRejectsAnUndefinedHostEffect(int value)
	{
		ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
			new CheatEngineFailure(CheatEngineFailureKind.Unknown, "Operation", "message", null,
				(CheatEngineHostEffect) value));

		Assert.Equal("hostEffect", exception.ParamName);
	}

	/// <summary>The indeterminate SDK 1.0.0 result is an ordinary operation failure, never a lifecycle fault.</summary>
	[Fact]
	[Trait("Qualification", "Q27")]
	public void IndeterminateHostResultThrowsTheOperationException()
	{
		CheatEngineFailure failure = new(CheatEngineFailureKind.IndeterminateHostResult, "Patterns.Scan",
			"Cheat Engine returned no AOB result list.", null, CheatEngineHostEffect.Completed);

		CheatEngineOperationException exception = Assert.Throws<CheatEngineOperationException>(failure.Throw);

		Assert.Equal(failure, exception.Failure);
		Assert.Equal(16, (int) CheatEngineFailureKind.IndeterminateHostResult);
	}

	/// <summary>The dedicated lifecycle exceptions keep the complete failure, including its host effect.</summary>
	[Theory]
	[InlineData(CheatEngineFailureKind.ActivationExpired)]
	[InlineData(CheatEngineFailureKind.InvalidState)]
	public void ThrowPreservesTheHostEffectInLifecycleExceptions(CheatEngineFailureKind kind)
	{
		CheatEngineFailure failure = new(kind, "Patterns.Scan", "The release was not confirmed.", null,
			CheatEngineHostEffect.CleanupUnconfirmed);

		CheatEngineClientException exception = Assert.ThrowsAny<CheatEngineClientException>(failure.Throw);

		Assert.Equal(failure, exception.Failure);
		Assert.Equal(CheatEngineHostEffect.CleanupUnconfirmed, exception.Failure.HostEffect);
	}

	/// <summary>Formatting the failure object, as structured loggers do, never emits user data.</summary>
	[Fact]
	[Trait("Qualification", "Q46")]
	public void ToStringOmitsMessageAndException()
	{
		InvalidOperationException innerException = new("lua: attempt to call nil 'secretGlobal' at C:\\Users\\player\\t.ct");
		CheatEngineFailure failure = new(CheatEngineFailureKind.LuaError, "Lua.ExecuteUnsafe",
			"The protected Lua call failed at 0x7FFC7A0A0000 while reading game.exe+1234.", innerException,
			CheatEngineHostEffect.Unknown);

		string text = failure.ToString();

		Assert.Equal("LuaError in Lua.ExecuteUnsafe (host effect: Unknown)", text);
		Assert.DoesNotContain("0x7FFC", text, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("game.exe", text, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("secretGlobal", text, StringComparison.Ordinal);
		Assert.DoesNotContain(nameof(InvalidOperationException), text, StringComparison.Ordinal);
		Assert.Equal("Unknown in <no operation> (host effect: Unknown)", default(CheatEngineFailure).ToString());
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
