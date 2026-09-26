namespace CheatEngine.Client.Results;

/// <summary>
///     Thrown for a <see cref="CheatEngineFailureKind.ActivationExpired" /> failure: a client, lease or context is used
///     after its plugin activation ended.
/// </summary>
/// <remarks>
///     <see cref="CheatEngineFailure.Throw(CancellationToken)" /> throws it and
///     <see cref="CheatEngineFailure.ToException(CancellationToken)" /> creates it; it has no public constructor.
/// </remarks>
public sealed class CheatEngineActivationExpiredException : CheatEngineClientException
{
	/// <summary>Creates the exception from an activation-expired failure without losing its host effect.</summary>
	/// <param name="failure">A failure whose kind is <see cref="CheatEngineFailureKind.ActivationExpired" />.</param>
	internal CheatEngineActivationExpiredException(CheatEngineFailure failure)
		: base(failure)
	{
	}
}
