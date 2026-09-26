namespace CheatEngine.Client.Runtime;

/// <summary>Describes the observed availability of a Client-owned high-level capability.</summary>
public enum ClientCapabilityAvailabilityState
{
	/// <summary>The Client has not established every prerequisite for the capability.</summary>
	Unknown = 0,

	/// <summary>The Client has established that the capability is available for this activation.</summary>
	Available = 1,

	/// <summary>The Client explicitly does not offer the capability for this activation.</summary>
	Unavailable = 2
}
