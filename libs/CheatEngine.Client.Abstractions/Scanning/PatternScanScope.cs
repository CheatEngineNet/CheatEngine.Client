namespace CheatEngine.Client.Scanning;

/// <summary>Describes which part of the target Cheat Engine actually scanned for an AOB request.</summary>
/// <remarks>
///     The value names the Cheat Engine work, not the Client filters. A module or range request can still produce
///     <see cref="GlobalHostScanWithManagedFilter" /> when the bounded route could not run: the filters then reduce only
///     what Core copies, never Cheat Engine's scan time or memory.
/// </remarks>
public enum PatternScanScope
{
	/// <summary>The scan scope was not reported.</summary>
	Unknown = 0,

	/// <summary>
	///     Cheat Engine ran one global <c>AOBScan</c> over the whole target for a request without a module or range.
	/// </summary>
	GlobalHostScan = 1,

	/// <summary>
	///     Cheat Engine ran a bounded, exhaustive MemScan over the requested module intersected with the requested range:
	///     its work was limited to those bounds.
	/// </summary>
	HostBoundedRange = 2,

	/// <summary>
	///     Cheat Engine ran one global <c>AOBScan</c> over the whole target for a module or range request whose bounded
	///     route could not run; Core applied the module and range filters as managed post-filters while copying.
	/// </summary>
	GlobalHostScanWithManagedFilter = 3
}
