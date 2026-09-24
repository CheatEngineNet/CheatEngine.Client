using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Scanning.Aob;

namespace CheatEngine.Client.Scanning;

/// <summary>An immutable AOB scan request with an explicit materialization limit and an optional module and range scope.</summary>
public readonly record struct AobScanRequest
{
	/// <summary>Creates an AOB scan request.</summary>
	public AobScanRequest(AobPattern pattern, AobScanOptions options, int maximumResults, ModuleName? module = null,
		AobScanRange? range = null)
	{
		if (string.IsNullOrWhiteSpace(pattern.Value))
		{
			throw new ArgumentException("An AOB pattern must be non-empty.", nameof(pattern));
		}

		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumResults);
		if (module.HasValue && string.IsNullOrWhiteSpace(module.Value.Value))
		{
			throw new ArgumentException("An AOB module filter must be non-empty.", nameof(module));
		}

		Pattern = pattern;
		Options = AobScanOptionsNormalizer.Normalize(options);
		MaximumResults = maximumResults;
		Module = module;
		Range = range;
	}

	/// <summary>Gets the scan pattern.</summary>
	public AobPattern Pattern
	{
		get;
	}

	/// <summary>Gets the SDK's evidence-backed optional scan arguments.</summary>
	public AobScanOptions Options
	{
		get;
	}

	/// <summary>Gets the maximum number of copied addresses that may survive managed post-filters.</summary>
	/// <remarks>
	///     This is the materialization limit: it bounds how many addresses Core copies from Cheat Engine's result. It does
	///     not bound or terminate the Cheat Engine scan, and it is not the number of available results (see
	///     <see cref="PatternScanMetrics.HostMatchCount" />). The bounded route copies at most 65,535 addresses, whatever
	///     this limit.
	/// </remarks>
	public int MaximumResults
	{
		get;
	}

	/// <summary>Gets the optional module that scopes the scan; Core resolves it before any scan.</summary>
	/// <remarks>
	///     On a qualified local target Cheat Engine scans only the module (intersected with <see cref="Range" />,
	///     <see cref="PatternScanScope.HostBoundedRange" />), and a match must lie entirely inside it. Otherwise Cheat Engine
	///     scans the whole target and Core keeps the addresses that start inside the module
	///     (<see cref="PatternScanScope.GlobalHostScanWithManagedFilter" />).
	/// </remarks>
	public ModuleName? Module
	{
		get;
	}

	/// <summary>Gets the optional inclusive range of match start addresses that scopes the scan.</summary>
	/// <remarks>
	///     On a qualified local target Cheat Engine scans only <c>[Start, End + pattern length)</c>, intersected with
	///     <see cref="Module" /> (<see cref="PatternScanScope.HostBoundedRange" />); otherwise the range is applied while
	///     copying and does not reduce Cheat Engine's scan time or memory
	///     (<see cref="PatternScanScope.GlobalHostScanWithManagedFilter" />).
	/// </remarks>
	public AobScanRange? Range
	{
		get;
	}
}
