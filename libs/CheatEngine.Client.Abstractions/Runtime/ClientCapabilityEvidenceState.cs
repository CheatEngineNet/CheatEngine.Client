namespace CheatEngine.Client.Runtime;

/// <summary>Describes one independently established prerequisite in a Client capability evidence record.</summary>
public enum ClientCapabilityEvidenceState
{
	/// <summary>The Client has not established this prerequisite for the current observation.</summary>
	Unknown = 0,

	/// <summary>The prerequisite was established for the current observation.</summary>
	Satisfied = 1,

	/// <summary>The prerequisite is known to be absent or denied.</summary>
	Missing = 2,

	/// <summary>The prerequisite probe reached the host but failed before it could establish a result.</summary>
	Faulted = 3,

	/// <summary>The host returned a result that cannot satisfy the documented prerequisite shape.</summary>
	Malformed = 4
}
