using CheatEngine.SDK.Engine.Scanning.Aob;
using CheatEngine.SDK.Engine.Scanning.Values;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Domains;

/// <summary>
///     A copied CheatEngine.SDK <c>AobBoundedScanResult</c>: the outcome of one bounded, exhaustive AOB scan, its copy
///     accounting, Cheat Engine's error text and the release of its MemScan session.
/// </summary>
/// <remarks>
///     The SDK result and its release outcome have internal constructors, so the port copies them into this value and
///     <see cref="PatternScanner" /> is testable without a host. The addresses themselves are written to the caller's
///     destination; <see cref="Written" /> says how many.
/// </remarks>
internal readonly record struct AobBoundedHostResult
{
	/// <summary>Gets the factual outcome category.</summary>
	public AobBoundedScanOutcomeKind Kind
	{
		get;
		init;
	}

	/// <summary>Gets the session factory status; <see cref="MemoryScanCreationStatus.Unknown" /> when none was attempted.</summary>
	public MemoryScanCreationStatus CreationStatus
	{
		get;
		init;
	}

	/// <summary>Gets the protected Lua status of <see cref="AobBoundedScanOutcomeKind.ScanFailed" />.</summary>
	public LuaStatus LuaStatus
	{
		get;
		init;
	}

	/// <summary>Gets the available results Cheat Engine reported, including rows outside the bounds.</summary>
	public ulong HostResultCount
	{
		get;
		init;
	}

	/// <summary>Gets how many in-bounds addresses were written to the destination; zero unless the scan succeeded.</summary>
	public int Written
	{
		get;
		init;
	}

	/// <summary>Gets how many rows were read.</summary>
	public ulong RowsRead
	{
		get;
		init;
	}

	/// <summary>Gets how many available rows were not read.</summary>
	public ulong UnreadHostRows
	{
		get;
		init;
	}

	/// <summary>Gets how many returned addresses lay below the start bound and were dropped by the SDK.</summary>
	public ulong BelowStartSkipped
	{
		get;
		init;
	}

	/// <summary>Gets how many returned addresses lay at or above the stop bound and were dropped by the SDK.</summary>
	public ulong AtOrAfterStopSkipped
	{
		get;
		init;
	}

	/// <summary>Gets whether the destination filled up while unread rows remained.</summary>
	public bool IsMaterializationLimitReached
	{
		get;
		init;
	}

	/// <summary>Gets Cheat Engine's non-empty error text, bounded by the SDK and never parsed.</summary>
	public string? HostErrorText
	{
		get;
		init;
	}

	/// <summary>Gets whether <see cref="HostErrorText" /> is a prefix of a longer text.</summary>
	public bool IsHostErrorTextTruncated
	{
		get;
		init;
	}

	/// <summary>Gets whether reading Cheat Engine's error text failed.</summary>
	public bool IsHostErrorTextUnreadable
	{
		get;
		init;
	}

	/// <summary>Gets the time from the first-scan call to the end of the successful wait; zero when the scan did not complete.</summary>
	public TimeSpan HostScanElapsed
	{
		get;
		init;
	}

	/// <summary>Gets the time the SDK spent reading the count, the error text and the rows.</summary>
	public TimeSpan CopyElapsed
	{
		get;
		init;
	}

	/// <summary>Gets the release status of the session's found list.</summary>
	public TargetReleaseStatus FoundListRelease
	{
		get;
		init;
	}

	/// <summary>Gets the release status of the session's scanner.</summary>
	public TargetReleaseStatus MemScanRelease
	{
		get;
		init;
	}

	/// <summary>Gets how a scan that may still have been running was stopped before the session was released.</summary>
	public MemoryScanTerminationStatus ReleaseTermination
	{
		get;
		init;
	}
}
