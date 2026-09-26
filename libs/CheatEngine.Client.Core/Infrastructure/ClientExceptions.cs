using CheatEngine.Client.Results;

namespace CheatEngine.Client.Core.Infrastructure;

/// <summary>Creates the Client exceptions that Core throws, always through the failure they carry.</summary>
/// <remarks>
///     No Client exception has a public constructor: every one is created by
///     <see cref="CheatEngineFailure.ToException(CancellationToken)" />, so its type follows the failure kind and it keeps
///     the failure's <see cref="CheatEngineFailure.HostEffect" />. An admission or lifetime refusal happens before any
///     Cheat Engine work, so its effect is <see cref="CheatEngineHostEffect.NotStarted" /> unless the caller knows more.
/// </remarks>
internal static class ClientExceptions
{
	/// <summary>Creates the exception of an <see cref="CheatEngineFailureKind.InvalidState" /> failure.</summary>
	/// <param name="operation">The public operation name.</param>
	/// <param name="message">The diagnostic message.</param>
	/// <param name="exception">The originating exception, if any.</param>
	/// <param name="hostEffect">How far Cheat Engine work got; nothing started by default.</param>
	/// <returns>A <see cref="CheatEngineInvalidStateException" />.</returns>
	internal static Exception InvalidState(string operation, string message, Exception? exception = null,
		CheatEngineHostEffect hostEffect = CheatEngineHostEffect.NotStarted)
	{
		return new CheatEngineFailure(CheatEngineFailureKind.InvalidState, operation, message, exception, hostEffect)
			.ToException();
	}

	/// <summary>Creates the exception of an <see cref="CheatEngineFailureKind.ActivationExpired" /> failure.</summary>
	/// <param name="operation">The public operation name.</param>
	/// <param name="message">The diagnostic message.</param>
	/// <param name="exception">The originating exception, if any.</param>
	/// <param name="hostEffect">How far Cheat Engine work got; nothing started by default.</param>
	/// <returns>A <see cref="CheatEngineActivationExpiredException" />.</returns>
	internal static Exception ActivationExpired(string operation, string message, Exception? exception = null,
		CheatEngineHostEffect hostEffect = CheatEngineHostEffect.NotStarted)
	{
		return new CheatEngineFailure(CheatEngineFailureKind.ActivationExpired, operation, message, exception,
			hostEffect).ToException();
	}
}
