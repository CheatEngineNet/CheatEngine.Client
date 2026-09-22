namespace CheatEngine.Client.Results;

/// <summary>Thrown when a resource is used after its plugin activation epoch has ended.</summary>
public sealed class CheatEngineActivationExpiredException : CheatEngineClientException
{
	/// <summary>Creates an expired-activation exception for the attempted operation.</summary>
	public CheatEngineActivationExpiredException(string operation, string message, Exception? innerException = null)
		: base(new CheatEngineFailure(CheatEngineFailureKind.ActivationExpired, operation, message, innerException))
	{
	}

	/// <summary>Creates the exception from an existing activation-expired failure without losing its host effect.</summary>
	internal CheatEngineActivationExpiredException(CheatEngineFailure failure)
		: base(failure)
	{
	}
}
