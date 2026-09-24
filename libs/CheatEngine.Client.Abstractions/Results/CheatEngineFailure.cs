using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace CheatEngine.Client.Results;

/// <summary>An immutable description of an expected Cheat Engine operation failure.</summary>
/// <remarks>
///     <para>
///         <see cref="Kind" /> classifies why the operation failed and <see cref="HostEffect" /> states how far the
///         requested Cheat Engine primitive got. Both are stable, language-independent values: never classify a failure by
///         parsing <see cref="Message" /> or <see cref="Exception" /> text.
///     </para>
///     <para>
///         <b>Diagnostics and redaction (Q46).</b> <see cref="Kind" />, <see cref="Operation" /> and
///         <see cref="HostEffect" /> are safe to log. <see cref="Message" /> and <see cref="Exception" /> are user data:
///         they can contain addresses, values, symbol expressions, module names, file paths, or Lua source and error text.
///         Log them only on an explicit opt-in chosen by the application. <see cref="ToString" /> returns only the safe
///         fields, so a structured logger that formats the failure object does not emit user data by default.
///     </para>
/// </remarks>
public readonly record struct CheatEngineFailure
{
	private readonly string? _message;
	private readonly string? _operation;

	/// <summary>Creates a failure that states its category, the failed operation and what is known of its host effect.</summary>
	/// <param name="kind">The stable failure category.</param>
	/// <param name="operation">The Client operation that failed, for example <c>Patterns.Scan</c>.</param>
	/// <param name="message">A human-readable diagnostic message; it may contain user data.</param>
	/// <param name="exception">The originating exception, if any; it may contain user data.</param>
	/// <param name="hostEffect">
	///     What is known about the Cheat Engine side effect of the failed operation; the conservative
	///     <see cref="CheatEngineHostEffect.Unknown" /> when omitted.
	/// </param>
	/// <exception cref="ArgumentException">
	///     <paramref name="operation" /> or <paramref name="message" /> is <see langword="null" />, empty or white space.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="hostEffect" /> is not a defined value.</exception>
	public CheatEngineFailure(CheatEngineFailureKind kind, string operation, string message,
		Exception? exception = null, CheatEngineHostEffect hostEffect = CheatEngineHostEffect.Unknown)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(operation);
		ArgumentException.ThrowIfNullOrWhiteSpace(message);
		if (!Enum.IsDefined(hostEffect))
		{
			throw new ArgumentOutOfRangeException(nameof(hostEffect), hostEffect,
				"The Cheat Engine host effect must be a defined value.");
		}

		Kind = kind;
		_operation = operation;
		_message = message;
		Exception = exception;
		HostEffect = hostEffect;
	}

	/// <summary>Gets the stable failure category.</summary>
	/// <remarks><see cref="CheatEngineFailureKind.Unknown" /> for the <see langword="default" /> value.</remarks>
	public CheatEngineFailureKind Kind
	{
		get;
	}

	/// <summary>Gets the Client operation that failed.</summary>
	/// <remarks>Never <see langword="null" />: <see cref="string.Empty" /> for the <see langword="default" /> value.</remarks>
	public string Operation => _operation ?? string.Empty;

	/// <summary>Gets a human-readable diagnostic message.</summary>
	/// <remarks>
	///     The message may contain user data (addresses, expressions, paths, Lua text); do not log it by default. Its text
	///     is not part of the contract and can change in any release. Never <see langword="null" />:
	///     <see cref="string.Empty" /> for the <see langword="default" /> value.
	/// </remarks>
	public string Message => _message ?? string.Empty;

	/// <summary>Gets whether this value is the <see langword="default" /> failure, which describes no failure.</summary>
	/// <remarks>
	///     No Client operation returns the <see langword="default" /> value as a failure: a <c>Try</c> method leaves its
	///     <c>failure</c> output <see langword="default" /> only when it returns <see langword="true" />. Reading a
	///     <see langword="default" /> value is safe: <see cref="Operation" /> and <see cref="Message" /> are empty,
	///     <see cref="Kind" /> and <see cref="HostEffect" /> are <c>Unknown</c>, and <see cref="Exception" /> is
	///     <see langword="null" />. Only <see cref="Throw(CancellationToken)" /> rejects it.
	/// </remarks>
	public bool IsDefault => _operation is null;

	/// <summary>Gets the originating SDK exception when one exists.</summary>
	/// <remarks>The exception may contain user data; do not log it by default.</remarks>
	public Exception? Exception
	{
		get;
	}

	/// <summary>Gets what is known about the Cheat Engine side effect of the failed operation.</summary>
	/// <remarks>
	///     <see cref="CheatEngineHostEffect.Unknown" /> is the conservative default: any effect is possible. A cancellation
	///     token never interrupts a Cheat Engine call that has already started.
	/// </remarks>
	public CheatEngineHostEffect HostEffect
	{
		get;
	}

	/// <summary>Throws this failure as the exception its kind maps to.</summary>
	/// <param name="cancellationToken">
	///     The token the failed operation observed. It is recorded on the thrown
	///     <see cref="CheatEngineOperationCanceledException" /> when the failure is
	///     <see cref="CheatEngineFailureKind.Cancelled" />, so that a caller can match it with the token it passed.
	/// </param>
	/// <remarks>
	///     <list type="bullet">
	///         <item>
	///             <see cref="CheatEngineFailureKind.Cancelled" /> throws <see cref="CheatEngineOperationCanceledException" />,
	///             an <see cref="OperationCanceledException" />.
	///         </item>
	///         <item>
	///             <see cref="CheatEngineFailureKind.ActivationExpired" /> throws
	///             <see cref="CheatEngineActivationExpiredException" />.
	///         </item>
	///         <item>
	///             <see cref="CheatEngineFailureKind.InvalidState" /> throws <see cref="CheatEngineClientLifecycleException" />.
	///         </item>
	///         <item>
	///             Every other kind, including a kind this version does not define, throws
	///             <see cref="CheatEngineOperationException" />.
	///         </item>
	///         <item>
	///             A <see langword="default" /> failure, which no operation produces, throws
	///             <see cref="InvalidOperationException" />: it describes no failure.
	///         </item>
	///     </list>
	///     The thrown exception's <c>Failure</c> equals this failure, including its <see cref="HostEffect" />. Every
	///     throwing convenience operation of the Client throws through this method.
	/// </remarks>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     The failure is <see cref="CheatEngineFailureKind.Cancelled" />.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">
	///     The failure is <see cref="CheatEngineFailureKind.ActivationExpired" />.
	/// </exception>
	/// <exception cref="CheatEngineClientLifecycleException">
	///     The failure is <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">The failure has any other kind.</exception>
	/// <exception cref="InvalidOperationException">The failure is the <see langword="default" /> value.</exception>
	[DoesNotReturn]
	[StackTraceHidden]
	public readonly void Throw(CancellationToken cancellationToken = default)
	{
		if (IsDefault)
		{
			throw new InvalidOperationException(
				"A default CheatEngineFailure describes no failure and cannot be thrown.");
		}

		switch (Kind)
		{
			case CheatEngineFailureKind.Cancelled:
				throw new CheatEngineOperationCanceledException(this, cancellationToken);
			case CheatEngineFailureKind.ActivationExpired:
				throw new CheatEngineActivationExpiredException(this);
			case CheatEngineFailureKind.InvalidState:
				throw new CheatEngineClientLifecycleException(this);
			default:
				throw new CheatEngineOperationException(this);
		}
	}

	/// <summary>Returns only the fields that are safe to log: kind, operation, and host effect.</summary>
	/// <returns>A redaction-safe description that never contains <see cref="Message" /> or <see cref="Exception" />.</returns>
	public override string ToString()
	{
		return $"{Kind} in {(IsDefault ? "<no operation>" : Operation)} (host effect: {HostEffect})";
	}
}
