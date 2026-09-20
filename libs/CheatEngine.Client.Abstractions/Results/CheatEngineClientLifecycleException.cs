namespace CheatEngine.Client.Results;

/// <summary>Thrown when code uses a client, session, or handle outside its active plugin lifecycle.</summary>
public sealed class CheatEngineClientLifecycleException : CheatEngineClientException
{
	/// <summary>Creates a lifecycle exception with the operation that was attempted.</summary>
	public CheatEngineClientLifecycleException(string operation, string message, Exception? innerException = null)
		: base(new CheatEngineFailure(CheatEngineFailureKind.InvalidState, operation, message, innerException))
	{
	}
}
