namespace CheatEngine.Client.Allocations;

/// <summary>Describes the access required by a Client-owned target-memory allocation.</summary>
public enum TargetAllocationAccess
{
	/// <summary>Creates memory for copied parameter and data exchange.</summary>
	ReadWrite,

	/// <summary>Creates executable memory when a capability-gated operation has proven that requirement.</summary>
	ExecuteReadWrite
}
