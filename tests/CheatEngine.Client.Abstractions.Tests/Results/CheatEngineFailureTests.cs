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

		CheatEngineInvalidStateException exception =
			Assert.Throws<CheatEngineInvalidStateException>(() => failure.Throw(CancellationToken.None));

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

		Assert.IsType<OperationCanceledException>(exception, exactMatch: false);
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
	[InlineData(CheatEngineFailureKind.InvalidState, typeof(CheatEngineInvalidStateException))]
	[InlineData(CheatEngineFailureKind.RuntimeChanged, typeof(CheatEngineOperationException))]
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
		CheatEngineFailure failure = new(CheatEngineFailureKind.LuaError, "UnsafeLua.Execute",
			"The protected Lua call failed at 0x7FFC7A0A0000 while reading game.exe+1234.", innerException,
			CheatEngineHostEffect.Unknown);

		string text = failure.ToString();

		Assert.Equal("LuaError in UnsafeLua.Execute (host effect: Unknown)", text);
		Assert.DoesNotContain("0x7FFC", text, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("game.exe", text, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("secretGlobal", text, StringComparison.Ordinal);
		Assert.DoesNotContain(nameof(InvalidOperationException), text, StringComparison.Ordinal);
		Assert.Equal("Unknown in <no operation> (host effect: Unknown)", default(CheatEngineFailure).ToString());
	}

	/// <summary>
	///     <see cref="CheatEngineFailure.ToException" /> creates, without throwing, the exception that
	///     <see cref="CheatEngineFailure.Throw" /> throws: its type follows the kind and it keeps the complete failure.
	/// </summary>
	[Theory]
	[InlineData(CheatEngineFailureKind.Cancelled, typeof(CheatEngineOperationCanceledException))]
	[InlineData(CheatEngineFailureKind.ActivationExpired, typeof(CheatEngineActivationExpiredException))]
	[InlineData(CheatEngineFailureKind.InvalidState, typeof(CheatEngineInvalidStateException))]
	[InlineData(CheatEngineFailureKind.NotFound, typeof(CheatEngineOperationException))]
	[InlineData(CheatEngineFailureKind.Unknown, typeof(CheatEngineOperationException))]
	public void ToExceptionCreatesTheExceptionThatThrowThrows(CheatEngineFailureKind kind, Type expected)
	{
		CancellationToken token = TestContext.Current.CancellationToken;
		CheatEngineFailure failure = new(kind, "Table.Read", "The operation failed.", null,
			CheatEngineHostEffect.NotStarted);

		Exception created = failure.ToException(token);
		Exception thrown = Assert.ThrowsAny<Exception>(() => failure.Throw(token));

		Assert.IsType(expected, created);
		Assert.IsType(expected, thrown);
		CheatEngineFailure carried = created is CheatEngineOperationCanceledException cancelled
			? cancelled.Failure
			: ((CheatEngineClientException) created).Failure;
		Assert.Equal(failure, carried);
		Assert.Equal(CheatEngineHostEffect.NotStarted, carried.HostEffect);
		if (created is OperationCanceledException canceled)
		{
			Assert.Equal(token, canceled.CancellationToken);
		}
	}

	[Fact]
	public void ADefaultFailureHasNoException()
	{
		Assert.Throws<InvalidOperationException>(() =>
			default(CheatEngineFailure).ToException(TestContext.Current.CancellationToken));
	}

	[Fact]
	public void AnUndefinedKindIsRejected()
	{
		ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
			new CheatEngineFailure((CheatEngineFailureKind) 999, "Table.Read", "The operation failed."));

		Assert.Equal("kind", exception.ParamName);
	}

	/// <summary>No Client exception has a public or protected constructor: a failure is the only way to one.</summary>
	[Fact]
	public void NoClientExceptionHasAPublicOrProtectedConstructor()
	{
		Type[] exceptions =
		[
			typeof(CheatEngineClientException), typeof(CheatEngineActivationExpiredException),
			typeof(CheatEngineInvalidStateException), typeof(CheatEngineOperationException),
			typeof(CheatEngineOperationCanceledException)
		];

		Assert.All(exceptions, static type => Assert.DoesNotContain(
			type.GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
								 System.Reflection.BindingFlags.Instance),
			static constructor => constructor.IsPublic || constructor.IsFamily || constructor.IsFamilyOrAssembly));
		Assert.True(typeof(CheatEngineClientException).IsAbstract);
	}
}
