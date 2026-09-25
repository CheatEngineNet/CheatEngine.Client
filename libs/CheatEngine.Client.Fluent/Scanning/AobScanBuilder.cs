using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Scanning;

/// <summary>An immutable, handle-free AOB scan builder before its result cardinality is selected.</summary>
/// <remarks>
///     <para>
///         Start it with <see cref="CheatEngineAobFluentExtensions.Aob(IPatternScanner, string)" />, for example
///         <c>client.Patterns.Aob(pattern)</c>: the builder is bound to that scanner and never rebound. It is a plain
///         value that declares no <c>Equals</c>, <c>GetHashCode</c>, <c>ToString</c> or equality operators (only those
///         inherited from <see cref="ValueType" />): compare the requests it builds, not the builders. Its only
///         constructor is the implicit parameterless one, which yields the <see langword="default" /> value: that value
///         has no pattern scanner, and <see cref="RequireSingle" />, <see cref="FirstOrNone" /> and <see cref="Take" />
///         throw <see cref="InvalidOperationException" /> on it.
///     </para>
///     <para>
///         <see cref="InModule(string)" /> and <see cref="InRange" /> scope the scan with one rule on every route: a match
///         must lie entirely inside the module, and its start must lie in the range. On a qualified local target Cheat
///         Engine scans only the module intersected with the range (a bounded MemScan that blocks Cheat Engine's main
///         thread and cannot be interrupted once started); otherwise Cheat Engine runs one global <c>AOBScan</c> and Core
///         applies the same rule while copying, which does not reduce Cheat Engine's scan time or memory.
///         <see cref="Take" />, <see cref="FirstOrNone" /> (1) and <see cref="RequireSingle" /> (2) bound only how many
///         addresses Core copies (never more than 65,535, a cut reported as <see cref="AobScanResult.IsTruncated" />);
///         they never stop Cheat Engine early.
///     </para>
///     <para>
///         <see cref="FirstOrNone" /> returns the first element in Cheat Engine's result-list order, which Cheat Engine
///         does not specify: not the lowest address and not the first logical region. <see cref="RequireSingle" /> copies
///         up to two matches from an exhaustive scan (global, or bounded and exhaustive): two copied matches are
///         ambiguous, and one copied match is unique only when every row Cheat Engine returned was read and the copy is
///         not truncated; it is never backed by a "first found" scan.
///     </para>
///     <para>
///         <see langword="null" />, <see cref="CheatEngine.Client.Results.CheatEngineFailureKind.NotFound" /> and an
///         empty <see cref="Take" /> result are factual zeros: the scan succeeded, every row Cheat Engine returned was read
///         (<see cref="PatternScanMetrics.InBoundsCountIsExact" />), and none lay inside the request. Each route answers
///         as follows:
///     </para>
///     <list type="bullet">
///         <item>
///             <description>
///                 <see cref="PatternScanScope.HostBoundedRange" /> (a module or range on a qualified local target): its
///                 empty in-bounds result is a factual zero.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <see cref="PatternScanScope.GlobalHostScanWithManagedFilter" /> (a module or range on a CEServer or
///                 file-as-process target): a result list whose rows all lie outside the module or range is a factual
///                 zero.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <see cref="PatternScanScope.GlobalHostScan" /> (no module or range): only an empty list that Cheat
///                 Engine does return is a factual zero. On Cheat Engine 7.7 <c>AOBScan</c> returns <c>nil</c> for zero
///                 matches and for some host failures alike, which the scanner reports as
///                 <see cref="CheatEngine.Client.Results.CheatEngineFailureKind.IndeterminateHostResult" />.
///             </description>
///         </item>
///     </list>
///     <para>
///         A <c>nil</c> result and an empty copy that did not read every row are both
///         <see cref="CheatEngine.Client.Results.CheatEngineFailureKind.IndeterminateHostResult" />: neither is ever
///         <see langword="null" />, <see cref="CheatEngine.Client.Results.CheatEngineFailureKind.NotFound" /> or an empty
///         result. A cancellation token cannot interrupt a scan that Cheat Engine has started.
///     </para>
/// </remarks>
public readonly struct AobScanBuilder
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
	/// <exception cref="ArgumentException">
	///     <paramref name="moduleName" /> is <see langword="null" />, empty or white space.
	/// </exception>
	/// <remarks>
	///     A match is kept only when all of its pattern bytes lie inside the module, on every route. On a qualified local
	///     target Cheat Engine scans only the module; otherwise Cheat Engine scans the whole target and Core applies the
	///     same rule while copying, at the cost of a global scan.
	/// </remarks>
	public AobScanBuilder InModule(string moduleName)
	{
		return InModule(new ModuleName(moduleName));
	}

	/// <summary>Returns an equivalent builder scoped to a target module.</summary>
	/// <param name="module">The module name Core resolves before any scan.</param>
	/// <returns>A new immutable builder.</returns>
	/// <exception cref="ArgumentException"><paramref name="module" /> is the empty default value.</exception>
	/// <remarks>
	///     A match is kept only when all of its pattern bytes lie inside the module, on every route. On a qualified local
	///     target Cheat Engine scans only the module; otherwise Cheat Engine scans the whole target and Core applies the
	///     same rule while copying, at the cost of a global scan.
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
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="end" /> precedes <paramref name="start" />.</exception>
	/// <remarks>
	///     A match is kept when its start lies in the range, on every route (and, with a module, when it also fits entirely
	///     inside the module). On a qualified local target Cheat Engine scans only <c>[start, end + pattern length)</c>,
	///     intersected with the module. Otherwise the global scan is not narrowed and Core applies the range while copying,
	///     before the materialization limit is counted.
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
	/// <returns>An immutable single-match terminal builder bound to this builder's scanner.</returns>
	/// <exception cref="InvalidOperationException">
	///     This builder is the <see langword="default" /> value, which has no pattern scanner.
	/// </exception>
	/// <remarks>
	///     Core copies up to two matches from an exhaustive scan (the global scan or the exhaustive bounded scan): a second
	///     copied match is reported as ambiguous, and one copied match is unique only when every row Cheat Engine returned
	///     was read and the copy is not truncated; otherwise whether a second match exists is unknown, which is reported
	///     as <see cref="CheatEngine.Client.Results.CheatEngineFailureKind.IndeterminateHostResult" />. Uniqueness is never
	///     inferred from a "unique" or "first found" scan.
	/// </remarks>
	public AobSingleMatchBuilder RequireSingle()
	{
		return new AobSingleMatchBuilder(RequireScanner(), BuildRequest(2));
	}

	/// <summary>
	///     Selects an operation that returns the first copied match, or <see langword="null" /> when the scan proves that
	///     no match lies inside the request.
	/// </summary>
	/// <returns>An immutable first-match terminal builder bound to this builder's scanner.</returns>
	/// <exception cref="InvalidOperationException">
	///     This builder is the <see langword="default" /> value, which has no pattern scanner.
	/// </exception>
	/// <remarks>
	///     "First" means the first element in Cheat Engine's result-list order, which Cheat Engine does not specify; it is
	///     not guaranteed to be the lowest address or the first logical region. The copy limit of one never stops Cheat
	///     Engine's scan early, and the operation never uses a "first found" scan. <see langword="null" /> is returned only
	///     for a factual zero (see the remarks of <see cref="AobScanBuilder" />).
	/// </remarks>
	public AobFirstMatchBuilder FirstOrNone()
	{
		return new AobFirstMatchBuilder(RequireScanner(), BuildRequest(1));
	}

	/// <summary>Selects an operation that materializes no more than the requested number of matches.</summary>
	/// <param name="maximumResults">The positive maximum number of copied addresses.</param>
	/// <returns>An immutable bounded-result terminal builder bound to this builder's scanner.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="maximumResults" /> is zero or negative.</exception>
	/// <exception cref="InvalidOperationException">
	///     This builder is the <see langword="default" /> value, which has no pattern scanner.
	/// </exception>
	/// <remarks>
	///     This bound applies only while Core materializes the result. It is not pushed into Cheat Engine, does not
	///     request early termination, and does not reduce scan work. It is forwarded unchanged as
	///     <see cref="AobScanRequest.MaximumResults" />: every route copies at most 65,535 addresses, whatever this limit,
	///     and a result cut by that cap reports <see cref="AobScanResult.IsTruncated" />.
	/// </remarks>
	public AobManyMatchBuilder Take(int maximumResults)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumResults);
		return new AobManyMatchBuilder(RequireScanner(), BuildRequest(maximumResults));
	}

	private IPatternScanner RequireScanner()
	{
		return _scanner ?? throw new InvalidOperationException(
			"This AOB builder is a default value without a pattern scanner. Start it with scanner.Aob(pattern), " +
			"for example client.Patterns.Aob(pattern).");
	}

	private AobScanRequest BuildRequest(int maximumResults)
	{
		return new AobScanRequest(Pattern, maximumResults, Module, Range, Protection, Alignment);
	}
}
