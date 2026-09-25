namespace CheatEngine.Client.Results;

/// <summary>
///     Base type of the Client exceptions that carry a classified <see cref="CheatEngineFailure" />; a
///     <see cref="CheatEngineFailureKind.Cancelled" /> failure throws <see cref="CheatEngineOperationCanceledException" />,
///     an <see cref="OperationCanceledException" />, instead.
/// </summary>
/// <remarks>
///     No Client exception has a public constructor: <see cref="CheatEngineFailure.Throw(CancellationToken)" /> throws
///     one and <see cref="CheatEngineFailure.ToException(CancellationToken)" /> creates one, so the exception type always
///     follows <see cref="CheatEngineFailure.Kind" /> and every exception keeps the complete failure, including its
///     <see cref="CheatEngineFailure.HostEffect" />.
/// </remarks>
public abstract class CheatEngineClientException : Exception
{
	/// <summary>Creates an exception from a classified client failure.</summary>
	/// <param name="failure">The failure the exception carries.</param>
	private protected CheatEngineClientException(CheatEngineFailure failure)
		: base(failure.Message, failure.Exception)
	{
		Failure = failure;
	}

	/// <summary>Gets the failure that caused the exception, including its host effect.</summary>
	public CheatEngineFailure Failure
	{
		get;
	}
}
