using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Scanning;

/// <summary>An immutable, handle-free AOB scan builder before its result cardinality is selected.</summary>
/// <remarks>
///     <para>
///         <see cref="InModule(string)" /> and <see cref="InRange" /> scope the scan. On a qualified local target Cheat
///         Engine scans only the module intersected with the range (a bounded MemScan that blocks Cheat Engine's main
///         thread and cannot be interrupted once started); otherwise Cheat Engine runs one global <c>AOBScan</c> and Core
///         keeps only the addresses inside the module or range, which does not reduce Cheat Engine's scan time or memory.
///         <see cref="Take" />, <see cref="FirstOrNone" /> (1) and <see cref="RequireSingle" /> (2) bound only how many
///         addresses Core copies; they never stop Cheat Engine early.
///     </para>
///     <para>
///         <see cref="FirstOrNone" /> returns the first element in Cheat Engine's result-list order, which Cheat Engine
///         does not specify: not the lowest address and not the first logical region. <see cref="RequireSingle" /> copies
///         up to two matches from an exhaustive scan (global, or bounded and exhaustive), so a truncated result is
///         reported as ambiguous; it is never backed by a "first found" scan.
///     </para>
///     <para>
///         On a global route a scan that finds nothing is reported as
///         <see cref="CheatEngine.Client.Results.CheatEngineFailureKind.IndeterminateHostResult" />, not as
///         <see langword="null" /> or <see cref="CheatEngine.Client.Results.CheatEngineFailureKind.NotFound" />; only the
///         bounded route reports a factual zero. A cancellation token cannot interrupt a scan that Cheat Engine has
///         started.
///     </para>
/// </remarks>
public readonly record struct AobScanBuilder
{
	private readonly IPatternScanner? _scanner;

	internal AobScanBuilder(IPatternScanner? scanner, AobPattern pattern, ScanProtectionFilter protection,
		ScanAlignment alignment, ModuleName? module, AobScanRange? range)
	{
		_scanner = scanner;
		Pattern = pattern;
		Protection = protection;
		Alignment = alignment;
		Module = module;
		Range = range;
	}

	/// <summary>Gets the normalized pattern that the builder will submit to Cheat Engine.</summary>
	public AobPattern Pattern
	{
		get;
	}

	/// <summary>Gets the memory protection the matches must have; unspecified by default.</summary>
	public ScanProtectionFilter Protection
	{
		get;
	}

	/// <summary>Gets the alignment rule of candidate addresses; <see cref="ScanAlignment.None" /> by default.</summary>
	public ScanAlignment Alignment
	{
		get;
	}

	/// <summary>Gets the optional module name that scopes the scan; Core resolves it before any scan.</summary>
	public ModuleName? Module
	{
		get;
	}

	/// <summary>Gets the optional inclusive range of match start addresses that scopes the scan.</summary>
	public AobScanRange? Range
	{
		get;
	}

	/// <summary>Returns an equivalent builder scoped to a named module.</summary>
	/// <param name="moduleName">The non-empty module name that Core resolves before any scan.</param>
	/// <returns>A new immutable builder.</returns>
	/// <remarks>
	///     On a qualified local target Cheat Engine scans only the module, and a match must lie entirely inside it.
	///     Otherwise Cheat Engine scans the whole target and Core keeps the addresses that start inside the module, at the
	///     cost of a global scan.
	/// </remarks>
	public AobScanBuilder InModule(string moduleName)
	{
		return InModule(new ModuleName(moduleName));
	}

	/// <summary>Returns an equivalent builder scoped to a target module.</summary>
	/// <param name="module">The module name Core resolves before any scan.</param>
	/// <returns>A new immutable builder.</returns>
	/// <remarks>
	///     On a qualified local target Cheat Engine scans only the module, and a match must lie entirely inside it.
	///     Otherwise Cheat Engine scans the whole target and Core keeps the addresses that start inside the module, at the
	///     cost of a global scan.
	/// </remarks>
	public AobScanBuilder InModule(ModuleName module)
	{
		if (string.IsNullOrWhiteSpace(module.Value))
		{
			throw new ArgumentException("An AOB module filter must be non-empty.", nameof(module));
		}

		return new AobScanBuilder(_scanner, Pattern, Protection, Alignment, module, Range);
	}

	/// <summary>Returns an equivalent builder scoped to an inclusive range of match start addresses.</summary>
	/// <param name="start">The first allowed match start.</param>
	/// <param name="end">The last allowed match start.</param>
	/// <returns>A new immutable builder.</returns>
	/// <remarks>
	///     On a qualified local target Cheat Engine scans only <c>[start, end + pattern length)</c>, intersected with the
	///     module. Otherwise the global scan is not narrowed and Core applies the range while copying, before the
	///     materialization limit is counted.
	/// </remarks>
	public AobScanBuilder InRange(Address start, Address end)
	{
		return new AobScanBuilder(_scanner, Pattern, Protection, Alignment, Module, new AobScanRange(start, end));
	}

	/// <summary>Returns an equivalent builder that searches executable, non-copy-on-write, non-writable memory.</summary>
	/// <remarks>
	///     Cheat Engine's documented protection grammar does not expose a readable bit. <c>+X-C-W</c> therefore means
	///     executable, not copy-on-write, and not writable memory.
	/// </remarks>
	/// <returns>A new immutable builder whose protection filter replaces the current one.</returns>
	public AobScanBuilder Executable()
	{
		return WithProtection(new ScanProtectionFilter(ScanProtectionRequirement.Required,
			ScanProtectionRequirement.Excluded, ScanProtectionRequirement.Excluded));
	}

	/// <summary>Returns an equivalent builder that searches writable memory that is not copy-on-write.</summary>
	/// <remarks>
	///     This is Cheat Engine's <c>-C+W</c>: writable data, executable or not. Cheat Engine's protection grammar has no
	///     readable bit.
	/// </remarks>
	/// <returns>A new immutable builder whose protection filter replaces the current one.</returns>
	public AobScanBuilder Writable()
	{
		return WithProtection(new ScanProtectionFilter(ScanProtectionRequirement.Unspecified,
			ScanProtectionRequirement.Excluded, ScanProtectionRequirement.Required));
	}

	/// <summary>Returns an equivalent builder with an explicit protection filter.</summary>
	/// <param name="protection">The protection filter, one requirement per flag.</param>
	/// <returns>A new immutable builder whose protection filter replaces the current one.</returns>
	public AobScanBuilder WithProtection(ScanProtectionFilter protection)
	{
		return new AobScanBuilder(_scanner, Pattern, protection, Alignment, Module, Range);
	}

	/// <summary>Returns an equivalent builder that checks only addresses divisible by <paramref name="divisor" />.</summary>
	/// <param name="divisor">The positive divisor, for example 4 for 4-byte aligned matches.</param>
	/// <returns>A new immutable builder whose alignment rule replaces the current one.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="divisor" /> is zero or negative.</exception>
	public AobScanBuilder AlignedTo(int divisor)
	{
		return new AobScanBuilder(_scanner, Pattern, Protection, ScanAlignment.AlignedTo(divisor), Module, Range);
	}

	/// <summary>
	///     Returns an equivalent builder that checks only addresses whose hexadecimal text ends with
	///     <paramref name="digits" />.
	/// </summary>
	/// <param name="digits">One to sixteen hexadecimal digits, in either case.</param>
	/// <returns>A new immutable builder whose alignment rule replaces the current one.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="digits" /> is <see langword="null" />.</exception>
	/// <exception cref="ArgumentException"><paramref name="digits" /> is empty, too long or not hexadecimal.</exception>
	public AobScanBuilder WithLastDigits(string digits)
	{
		return new AobScanBuilder(_scanner, Pattern, Protection, ScanAlignment.LastDigits(digits), Module, Range);
	}

	/// <summary>Selects an operation that succeeds only when exactly one AOB match exists.</summary>
	/// <returns>An immutable single-match terminal builder.</returns>
	/// <remarks>
	///     Core copies up to two matches from an exhaustive scan (the global scan or the exhaustive bounded scan): a second
	///     match (a truncated copy) is reported as ambiguous. Uniqueness is never inferred from a "unique" or
	///     "first found" scan.
	/// </remarks>
	public AobSingleMatchBuilder RequireSingle()
	{
		return new AobSingleMatchBuilder(RequireScanner(), BuildRequest(2));
	}

	/// <summary>Selects an operation that returns the first copied match or <see langword="null" /> when there is none.</summary>
	/// <returns>An immutable first-match terminal builder.</returns>
	/// <remarks>
	///     "First" means the first element in Cheat Engine's result-list order, which Cheat Engine does not specify; it is
	///     not guaranteed to be the lowest address or the first logical region. The copy limit of one never stops Cheat
	///     Engine's scan early.
	/// </remarks>
	public AobFirstMatchBuilder FirstOrNone()
	{
		return new AobFirstMatchBuilder(RequireScanner(), BuildRequest(1));
	}

	/// <summary>Selects an operation that materializes no more than the requested number of matches.</summary>
	/// <param name="maximumResults">The positive maximum number of copied addresses.</param>
	/// <returns>An immutable bounded-result terminal builder.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="maximumResults" /> is zero or negative.</exception>
	/// <remarks>
	///     This bound applies only while Core materializes the result. It is not pushed into Cheat Engine, does not
	///     request early termination, and does not reduce scan work; the bounded route copies at most 65,535 addresses.
	/// </remarks>
	public AobManyMatchBuilder Take(int maximumResults)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumResults);
		return new AobManyMatchBuilder(RequireScanner(), BuildRequest(maximumResults));
	}

	private IPatternScanner RequireScanner()
	{
		return _scanner ?? throw new InvalidOperationException(
			"This AOB builder has no bound pattern scanner. Start the operation with client.Aob(pattern) or scanner.Aob(pattern).");
	}

	private AobScanRequest BuildRequest(int maximumResults)
	{
		return new AobScanRequest(Pattern, maximumResults, Module, Range, Protection, Alignment);
	}
}
