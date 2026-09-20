namespace CheatEngine.Client.Results;

/// <summary>Thrown when a throwing convenience operation observes an expected Cheat Engine failure.</summary>
public sealed class CheatEngineOperationException : CheatEngineClientException
{
	/// <summary>Creates an operation exception from a classified failure.</summary>
	public CheatEngineOperationException(CheatEngineFailure failure)
		: base(failure)
	{
	}
}
