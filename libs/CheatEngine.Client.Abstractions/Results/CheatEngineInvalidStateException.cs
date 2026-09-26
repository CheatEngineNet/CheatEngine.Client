namespace CheatEngine.Client.Results;

/// <summary>
///     Thrown for a <see cref="CheatEngineFailureKind.InvalidState" /> failure: the operation is not valid in the current
///     activation, session, resource or target state.
/// </summary>
/// <remarks>
///     <see cref="CheatEngineFailure.Throw(CancellationToken)" /> throws it and
///     <see cref="CheatEngineFailure.ToException(CancellationToken)" /> creates it; it has no public constructor.
/// </remarks>
public sealed class CheatEngineInvalidStateException : CheatEngineClientException
{
	/// <summary>Creates the exception from an invalid-state failure without losing its host effect.</summary>
	/// <param name="failure">A failure whose kind is <see cref="CheatEngineFailureKind.InvalidState" />.</param>
	internal CheatEngineInvalidStateException(CheatEngineFailure failure)
		: base(failure)
	{
	}
}
