namespace CheatEngine.Client.Results;

/// <summary>An immutable description of an expected Cheat Engine operation failure.</summary>
public readonly record struct CheatEngineFailure
{
	/// <summary>Creates a failure while retaining an optional SDK exception for diagnostics.</summary>
	public CheatEngineFailure(CheatEngineFailureKind kind, string operation, string message,
		Exception? exception = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(operation);
		ArgumentException.ThrowIfNullOrWhiteSpace(message);
		Kind = kind;
		Operation = operation;
		Message = message;
		Exception = exception;
	}

	/// <summary>Gets the stable failure category.</summary>
	public CheatEngineFailureKind Kind
	{
		get;
	}

	/// <summary>Gets the client operation that failed.</summary>
	public string Operation
	{
		get;
	}

	/// <summary>Gets a human-readable diagnostic message.</summary>
	public string Message
	{
		get;
	}

	/// <summary>Gets the originating SDK exception when one exists.</summary>
	public Exception? Exception
	{
		get;
	}

	/// <summary>Throws this failure as an operation exception.</summary>
	public readonly void Throw()
	{
		if (Kind == CheatEngineFailureKind.ActivationExpired)
		{
			throw new CheatEngineActivationExpiredException(Operation, Message, Exception);
		}

		throw new CheatEngineOperationException(this);
	}
}
