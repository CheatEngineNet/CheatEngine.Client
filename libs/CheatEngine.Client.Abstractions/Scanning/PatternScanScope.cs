namespace CheatEngine.Client.Scanning;

/// <summary>Describes which part of the target Cheat Engine actually scanned for an AOB request.</summary>
/// <remarks>
///     The value names the Cheat Engine work, not the Client filters. A module or range request can still produce
///     <see cref="GlobalHostScanWithManagedFilter" />: the filters then reduce only what Core copies, never Cheat Engine's
///     scan time or memory.
/// </remarks>
public enum PatternScanScope
{
	/// <summary>The scan scope was not reported.</summary>
	Unknown = 0,

	/// <summary>
	///     Cheat Engine ran one global <c>AOBScan</c> over the whole target; Core applied the module and range filters as
	///     managed post-filters while copying the addresses.
	/// </summary>
	GlobalHostScanWithManagedFilter = 1
}
