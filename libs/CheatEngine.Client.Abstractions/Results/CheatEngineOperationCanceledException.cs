namespace CheatEngine.Client.Results;

/// <summary>
///     Thrown by a throwing convenience operation when the caller's cancellation was observed: the operation failed with
///     <see cref="CheatEngineFailureKind.Cancelled" />.
/// </summary>
/// <remarks>
///     <para>
///         The exception derives from <see cref="OperationCanceledException" />, so the usual
///         <c>catch (OperationCanceledException)</c> handling applies, and
///         <see cref="OperationCanceledException.CancellationToken" /> is the token the operation observed.
///     </para>
///     <para>
///         <see cref="Failure" /> keeps the complete classified failure. Its <see cref="CheatEngineFailure.HostEffect" />
///         tells whether Cheat Engine work had started: a token never interrupts a Cheat Engine call that has already
///         begun and never removes an effect that such a call produced.
///     </para>
/// </remarks>
public sealed class CheatEngineOperationCanceledException : OperationCanceledException
{
	/// <summary>Creates the exception from a cancelled failure and the token the operation observed.</summary>
	/// <param name="failure">A failure whose kind is <see cref="CheatEngineFailureKind.Cancelled" />.</param>
	/// <param name="cancellationToken">The token the operation observed.</param>
	internal CheatEngineOperationCanceledException(CheatEngineFailure failure, CancellationToken cancellationToken)
		: base(failure.Message, failure.Exception, cancellationToken)
	{
		Failure = failure;
	}

	/// <summary>Gets the cancelled failure, including its host effect.</summary>
	public CheatEngineFailure Failure
	{
		get;
	}
}
