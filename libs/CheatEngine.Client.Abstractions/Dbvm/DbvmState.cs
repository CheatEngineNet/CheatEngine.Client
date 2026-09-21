namespace CheatEngine.Client.Dbvm;

/// <summary>Describes the observed DBVM lifecycle without triggering initialization.</summary>
public enum DbvmState
{
	/// <summary>No host observation has established the DBVM state.</summary>
	Unknown,

	/// <summary>The active host cannot expose DBVM operations.</summary>
	Unavailable,

	/// <summary>DBVM is supported but has not been initialized.</summary>
	NotInitialized,

	/// <summary>DBVM has been initialized explicitly for the active host.</summary>
	Initialized
}
