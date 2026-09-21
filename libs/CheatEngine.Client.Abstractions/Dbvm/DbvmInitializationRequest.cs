namespace CheatEngine.Client.Dbvm;

/// <summary>Describes an explicit DBVM initialization request.</summary>
public readonly record struct DbvmInitializationRequest
{
	/// <summary>Creates a DBVM initialization request.</summary>
	public DbvmInitializationRequest(bool useStealthMode = false)
	{
		UseStealthMode = useStealthMode;
	}

	/// <summary>Gets whether the caller explicitly requests the host's documented stealth mode.</summary>
	public bool UseStealthMode
	{
		get;
	}
}
