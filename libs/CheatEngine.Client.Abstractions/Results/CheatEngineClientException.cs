namespace CheatEngine.Client.Results;

/// <summary>Base exception for an invalid use of the Cheat Engine client contract.</summary>
public class CheatEngineClientException : Exception
{
	/// <summary>Creates an exception from a classified client failure.</summary>
	public CheatEngineClientException(CheatEngineFailure failure)
		: base(failure.Message, failure.Exception)
	{
		Failure = failure;
	}

	/// <summary>Gets the failure that caused the exception.</summary>
	public CheatEngineFailure Failure
	{
		get;
	}
}
