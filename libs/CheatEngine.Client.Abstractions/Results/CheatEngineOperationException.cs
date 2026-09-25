namespace CheatEngine.Client.Results;

/// <summary>
///     Thrown for every failure kind other than <see cref="CheatEngineFailureKind.Cancelled" />,
///     <see cref="CheatEngineFailureKind.ActivationExpired" /> and <see cref="CheatEngineFailureKind.InvalidState" />,
///     including a kind this version does not define: a throwing convenience operation observed an expected Cheat Engine
///     failure.
/// </summary>
/// <remarks>
///     <see cref="CheatEngineFailure.Throw(CancellationToken)" /> throws it and
///     <see cref="CheatEngineFailure.ToException(CancellationToken)" /> creates it; it has no public constructor.
/// </remarks>
public sealed class CheatEngineOperationException : CheatEngineClientException
{
	/// <summary>Creates an operation exception from a classified failure without losing its host effect.</summary>
	/// <param name="failure">The failure the exception carries.</param>
	internal CheatEngineOperationException(CheatEngineFailure failure)
		: base(failure)
	{
	}
}
