using CheatEngine.SDK.Engine.Inspection;

namespace CheatEngine.Client.Scanning;

/// <summary>An immutable AOB scan request with an explicit materialization limit and an optional module and range scope.</summary>
public readonly record struct AobScanRequest
{
	/// <summary>Creates an AOB scan request.</summary>
	/// <param name="pattern">The normalized pattern.</param>
	/// <param name="maximumResults">The positive materialization limit.</param>
	/// <param name="module">The optional module that scopes the scan.</param>
	/// <param name="range">The optional inclusive range of match start addresses that scopes the scan.</param>
	/// <param name="protection">The memory protection the matches must have; unspecified by default.</param>
	/// <param name="alignment">The alignment rule of candidate addresses; <see cref="ScanAlignment.None" /> by default.</param>
	/// <exception cref="ArgumentException">
	///     <paramref name="pattern" /> is empty, or <paramref name="module" /> is an empty module name.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="maximumResults" /> is zero or negative.</exception>
	public AobScanRequest(AobPattern pattern, int maximumResults, ModuleName? module = null, AobScanRange? range = null,
		ScanProtectionFilter protection = default, ScanAlignment alignment = default)
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
		MaximumResults = maximumResults;
		Module = module;
		Range = range;
		Protection = protection;
		Alignment = alignment;
	}

	/// <summary>Gets the scan pattern.</summary>
	public AobPattern Pattern
	{
		get;
	}

	/// <summary>Gets the maximum number of copied addresses that may survive managed post-filters.</summary>
	/// <remarks>
	///     This is the materialization limit: it bounds how many addresses Core copies from Cheat Engine's result. It does
	///     not bound or terminate the Cheat Engine scan, and it is not the number of available results (see
	///     <see cref="PatternScanMetrics.HostResultCount" />). The bounded route copies at most 65,535 addresses, whatever
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

	/// <summary>Gets the memory protection the matches must have.</summary>
	/// <remarks>Cheat Engine applies it on every route: it reduces the memory Cheat Engine scans.</remarks>
	public ScanProtectionFilter Protection
	{
		get;
	}

	/// <summary>Gets the alignment rule of candidate addresses.</summary>
	/// <remarks>Cheat Engine applies it on every route.</remarks>
	public ScanAlignment Alignment
	{
		get;
	}
}
