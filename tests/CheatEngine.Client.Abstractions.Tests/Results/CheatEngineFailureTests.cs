using System.Reflection;

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

		CheatEngineOperationException exception =
			Assert.Throws<CheatEngineOperationException>(() => failure.Throw(CancellationToken.None));

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
			Assert.Throws<CheatEngineActivationExpiredException>(() => failure.Throw(CancellationToken.None));

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
			Assert.Throws<CheatEngineClientLifecycleException>(() => failure.Throw(CancellationToken.None));

		Assert.Equal(failure, exception.Failure);
		Assert.Equal(failure.Message, exception.Message);
		Assert.Same(innerException, exception.InnerException);
	}

	/// <summary>Omitting the host effect keeps it conservative, and an explicit host effect round-trips.</summary>
	[Fact]
	public void HostEffectDefaultsToUnknownAndRoundTripsThroughTheConstructor()
	{
		InvalidOperationException innerException = new("release failed");

		CheatEngineFailure omitted = new(CheatEngineFailureKind.LuaError, "Lua.Execute", "Lua failed.");
		CheatEngineFailure explicitEffect = new(CheatEngineFailureKind.InvalidState, "Patterns.Scan",
			"The release was not confirmed.", innerException, CheatEngineHostEffect.CleanupUnconfirmed);

		Assert.Equal(CheatEngineHostEffect.Unknown, default(CheatEngineFailure).HostEffect);
		Assert.Equal(CheatEngineHostEffect.Unknown, omitted.HostEffect);
		Assert.Equal(CheatEngineHostEffect.CleanupUnconfirmed, explicitEffect.HostEffect);
		Assert.Equal(CheatEngineFailureKind.InvalidState, explicitEffect.Kind);
		Assert.Same(innerException, explicitEffect.Exception);
		Assert.NotEqual(explicitEffect, new CheatEngineFailure(explicitEffect.Kind, explicitEffect.Operation,
			explicitEffect.Message, innerException, CheatEngineHostEffect.Completed));
	}

	/// <summary>The failure has exactly one public constructor, whose trailing parameters are optional.</summary>
	[Fact]
	public void FailureHasExactlyOneConstructor()
	{
		ConstructorInfo constructor = Assert.Single(typeof(CheatEngineFailure).GetConstructors());

		Assert.Equal(["kind", "operation", "message", "exception", "hostEffect"],
			constructor.GetParameters().Select(static parameter => parameter.Name));
		Assert.Equal([false, false, false, true, true],
			constructor.GetParameters().Select(static parameter => parameter.IsOptional));
	}

	/// <summary>A default failure is safe to read: its strings are empty, never null, and it reports itself as default.</summary>
	[Fact]
	public void DefaultFailureIsSafeToReadAndReportsItself()
	{
		CheatEngineFailure failure = default;

		Assert.True(failure.IsDefault);
		Assert.Equal(string.Empty, failure.Operation);
		Assert.Equal(string.Empty, failure.Message);
		Assert.Equal(CheatEngineFailureKind.Unknown, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Unknown, failure.HostEffect);
		Assert.Null(failure.Exception);
		Assert.Equal("Unknown in <no operation> (host effect: Unknown)", failure.ToString());
	}

	/// <summary>Every constructed failure, even one of kind Unknown, is distinct from the default value.</summary>
	[Fact]
	public void ConstructedFailuresAreNeverDefault()
	{
		CheatEngineFailure unknown = new(CheatEngineFailureKind.Unknown, "Runtime.GetSnapshot", "Unclassified.");

		Assert.False(unknown.IsDefault);
		Assert.NotEqual(default, unknown);
		Assert.Equal("Runtime.GetSnapshot", unknown.Operation);
		Assert.Equal("Unclassified.", unknown.Message);
	}

	/// <summary>A null operation or message is rejected like an empty one.</summary>
	[Fact]
	public void ConstructorRejectsNullOperationOrMessage()
	{
		Assert.Throws<ArgumentNullException>(() => new CheatEngineFailure(CheatEngineFailureKind.Unknown, null!, "message"));
		Assert.Throws<ArgumentNullException>(() => new CheatEngineFailure(CheatEngineFailureKind.Unknown, "Operation", null!));
	}

	/// <summary>Rejects a host effect outside the documented vocabulary instead of storing an unclassifiable value.</summary>
	[Theory]
	[InlineData(-1)]
	[InlineData(6)]
	[InlineData(int.MaxValue)]
	public void ConstructorRejectsAnUndefinedHostEffect(int value)
	{
		ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
			new CheatEngineFailure(CheatEngineFailureKind.Unknown, "Operation", "message", null,
				(CheatEngineHostEffect) value));

		Assert.Equal("hostEffect", exception.ParamName);
	}

	/// <summary>The indeterminate AOB "no result list" outcome is an operation failure, never a lifecycle fault.</summary>
	[Fact]
	[Trait("Qualification", "Q27")]
	public void IndeterminateHostResultThrowsTheOperationException()
	{
		CheatEngineFailure failure = new(CheatEngineFailureKind.IndeterminateHostResult, "Patterns.Scan",
			"Cheat Engine returned no AOB result list.", null, CheatEngineHostEffect.Completed);

		CheatEngineOperationException exception =
			Assert.Throws<CheatEngineOperationException>(() => failure.Throw(CancellationToken.None));

		Assert.Equal(failure, exception.Failure);
		Assert.Equal(16, (int) CheatEngineFailureKind.IndeterminateHostResult);
	}

	/// <summary>The target, identity and runtime change kinds keep their published values after the 0 to 16 range.</summary>
	[Fact]
	public void TargetIdentityAndRuntimeChangeKindsHaveStableValues()
	{
		Assert.Equal(17, (int) CheatEngineFailureKind.TargetChanged);
		Assert.Equal(18, (int) CheatEngineFailureKind.TargetIdentityUnavailable);
		Assert.Equal(19, (int) CheatEngineFailureKind.RuntimeChanged);
		Assert.Equal(Enumerable.Range(0, 20), Enum.GetValues<CheatEngineFailureKind>().Select(static kind => (int) kind));
	}

	/// <summary>A target, identity or runtime change is an operation failure, never a lifecycle exception.</summary>
	[Theory]
	[InlineData(CheatEngineFailureKind.TargetChanged)]
	[InlineData(CheatEngineFailureKind.TargetIdentityUnavailable)]
	[InlineData(CheatEngineFailureKind.RuntimeChanged)]
	public void ChangeKindsThrowTheOperationException(CheatEngineFailureKind kind)
	{
		CheatEngineFailure failure = new(kind, "Patterns.Scan", "The target changed during the scan.", null,
			CheatEngineHostEffect.Completed);

		CheatEngineOperationException exception =
			Assert.Throws<CheatEngineOperationException>(() => failure.Throw(CancellationToken.None));

		Assert.Equal(failure, exception.Failure);
		Assert.Equal(kind, exception.Failure.Kind);
	}

	/// <summary>The documented negative result is a host effect of its own, with a published value.</summary>
	[Fact]
	public void NotAppliedIsADefinedHostEffectThatRoundTrips()
	{
		CheatEngineFailure failure = new(CheatEngineFailureKind.OperationRejected, "Tables.SetActive",
			"Cheat Engine refused the activation.", null, CheatEngineHostEffect.NotApplied);

		Assert.Equal(5, (int) CheatEngineHostEffect.NotApplied);
		Assert.Equal(CheatEngineHostEffect.NotApplied, failure.HostEffect);
		Assert.Equal("OperationRejected in Tables.SetActive (host effect: NotApplied)", failure.ToString());
		Assert.Equal(Enumerable.Range(0, 6), Enum.GetValues<CheatEngineHostEffect>().Select(static effect => (int) effect));
	}

	/// <summary>The dedicated lifecycle exceptions keep the complete failure, including its host effect.</summary>
	[Theory]
	[InlineData(CheatEngineFailureKind.ActivationExpired)]
	[InlineData(CheatEngineFailureKind.InvalidState)]
	public void ThrowPreservesTheHostEffectInLifecycleExceptions(CheatEngineFailureKind kind)
	{
		CheatEngineFailure failure = new(kind, "Patterns.Scan", "The release was not confirmed.", null,
			CheatEngineHostEffect.CleanupUnconfirmed);

		CheatEngineClientException exception =
			Assert.ThrowsAny<CheatEngineClientException>(() => failure.Throw(CancellationToken.None));

		Assert.Equal(failure, exception.Failure);
		Assert.Equal(CheatEngineHostEffect.CleanupUnconfirmed, exception.Failure.HostEffect);
	}

	/// <summary>
	///     A cancelled failure throws an <see cref="OperationCanceledException" /> that keeps the failure and the token the
	///     operation observed, whatever its host effect.
	/// </summary>
	[Theory]
	[InlineData(CheatEngineHostEffect.NotStarted)]
	[InlineData(CheatEngineHostEffect.Completed)]
	public void ThrowRaisesAnOperationCanceledExceptionForACancelledFailure(CheatEngineHostEffect hostEffect)
	{
		using CancellationTokenSource source = new();
		source.Cancel();
		InvalidOperationException innerException = new("observed after the copy");
		CheatEngineFailure failure = new(CheatEngineFailureKind.Cancelled, "Memory.Read",
			"The operation was cancelled.", innerException, hostEffect);

		CheatEngineOperationCanceledException exception =
			Assert.Throws<CheatEngineOperationCanceledException>(() => failure.Throw(source.Token));

		Assert.IsAssignableFrom<OperationCanceledException>(exception);
		Assert.Equal(failure, exception.Failure);
		Assert.Equal(hostEffect, exception.Failure.HostEffect);
		Assert.Equal(source.Token, exception.CancellationToken);
		Assert.Equal(failure.Message, exception.Message);
		Assert.Same(innerException, exception.InnerException);
	}

	/// <summary>With the empty token, the cancelled failure still throws the cancellation exception.</summary>
	[Fact]
	public void ThrowWithTheNoneTokenStillRaisesTheCancellationException()
	{
		CheatEngineFailure failure = new(CheatEngineFailureKind.Cancelled, "Patterns.Scan", "The scan was cancelled.",
			null, CheatEngineHostEffect.NotStarted);

		CheatEngineOperationCanceledException exception =
			Assert.Throws<CheatEngineOperationCanceledException>(() => failure.Throw(CancellationToken.None));

		Assert.Equal(CancellationToken.None, exception.CancellationToken);
		Assert.Equal(failure, exception.Failure);
	}

	/// <summary>Only a cancelled failure throws the cancellation exception; the token never changes the mapping.</summary>
	[Theory]
	[InlineData(CheatEngineFailureKind.Unknown, typeof(CheatEngineOperationException))]
	[InlineData(CheatEngineFailureKind.Cancelled, typeof(CheatEngineOperationCanceledException))]
	[InlineData(CheatEngineFailureKind.OperationRejected, typeof(CheatEngineOperationException))]
	[InlineData(CheatEngineFailureKind.ActivationExpired, typeof(CheatEngineActivationExpiredException))]
	[InlineData(CheatEngineFailureKind.InvalidState, typeof(CheatEngineClientLifecycleException))]
	[InlineData(CheatEngineFailureKind.RuntimeChanged, typeof(CheatEngineOperationException))]
	[InlineData((CheatEngineFailureKind) 1000, typeof(CheatEngineOperationException))]
	public void ThrowMapsEachKindToOneExceptionType(CheatEngineFailureKind kind, Type expected)
	{
		using CancellationTokenSource source = new();
		source.Cancel();
		CheatEngineFailure failure = new(kind, "Tables.Find", "The operation failed.", null,
			CheatEngineHostEffect.NotStarted);

		Exception exception = Assert.ThrowsAny<Exception>(() => failure.Throw(source.Token));

		Assert.IsType(expected, exception);
	}

	/// <summary>A default failure describes no failure: throwing it is a programming error, not an operation failure.</summary>
	[Fact]
	public void ThrowRejectsTheDefaultFailure()
	{
		CheatEngineFailure failure = default;

		InvalidOperationException exception =
			Assert.Throws<InvalidOperationException>(() => failure.Throw(CancellationToken.None));

		Assert.Null(exception.InnerException);
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
