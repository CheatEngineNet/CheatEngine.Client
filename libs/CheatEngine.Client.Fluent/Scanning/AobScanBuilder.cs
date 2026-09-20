using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Scanning.Aob;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Scanning;

/// <summary>An immutable, handle-free AOB scan builder before its result cardinality is selected.</summary>
public readonly record struct AobScanBuilder
{
	private readonly IPatternScanner? _scanner;

	internal AobScanBuilder(IPatternScanner? scanner, AobPattern pattern, AobScanOptions options, ModuleName? module,
		AobScanRange? range)
	{
		_scanner = scanner;
		Pattern = pattern;
		Options = options;
		Module = module;
		Range = range;
	}

	/// <summary>Gets the normalized pattern that the builder will submit to Cheat Engine.</summary>
	public AobPattern Pattern
	{
		get;
	}

	/// <summary>Gets the current evidence-backed Cheat Engine scan options.</summary>
	public AobScanOptions Options
	{
		get;
	}

	/// <summary>Gets the optional module-range filter that Core resolves before materializing matches.</summary>
	public ModuleName? Module
	{
		get;
	}

	/// <summary>Gets the optional inclusive copied-address range filter.</summary>
	public AobScanRange? Range
	{
		get;
	}

	/// <summary>Returns an equivalent builder that restricts results to one named target module.</summary>
	/// <param name="moduleName">The non-empty module name understood by Cheat Engine's symbol handler.</param>
	/// <returns>A new immutable builder.</returns>
	public AobScanBuilder InModule(string moduleName)
	{
		return InModule(new ModuleName(moduleName));
	}

	/// <summary>Returns an equivalent builder that restricts results to one target module.</summary>
	/// <param name="module">The module-range filter resolved by Core before result materialization.</param>
	/// <returns>A new immutable builder.</returns>
	public AobScanBuilder InModule(ModuleName module)
	{
		if (string.IsNullOrWhiteSpace(module.Value))
		{
			throw new ArgumentException("An AOB module filter must be non-empty.", nameof(module));
		}

		return new AobScanBuilder(_scanner, Pattern, Options, module, Range);
	}

	/// <summary>Returns an equivalent builder that retains only match addresses in an inclusive target-address range.</summary>
	/// <param name="start">The first included target address.</param>
	/// <param name="end">The last included target address.</param>
	/// <returns>A new immutable builder.</returns>
	/// <remarks>
	///     The SDK's string-form <c>AOBScan</c> binding has no start/end arguments. Core applies this range while
	///     copying the owned result list, before the requested materialization limit is counted.
	/// </remarks>
	public AobScanBuilder InRange(Address start, Address end)
	{
		return new AobScanBuilder(_scanner, Pattern, Options, Module, new AobScanRange(start, end));
	}

	/// <summary>Returns an equivalent builder that searches read-only executable memory.</summary>
	/// <remarks>
	///     Cheat Engine's documented protection grammar does not expose a readable bit. <c>+X-C-W</c> therefore means
	///     executable, not copy-on-write, and not writable memory.
	/// </remarks>
	/// <returns>A new immutable builder.</returns>
	public AobScanBuilder ReadableExecutable()
	{
		return WithOptions(new AobScanOptions("+X-C-W", Options.AlignmentMethod, Options.AlignmentParameter));
	}

	/// <summary>Returns an equivalent builder with the exact Cheat Engine protection expression.</summary>
	/// <param name="protectionFlags">
	///     The protection expression accepted by Cheat Engine, or <see langword="null" /> to omit
	///     it.
	/// </param>
	/// <returns>A new immutable builder.</returns>
	public AobScanBuilder WithProtectionFlags(string? protectionFlags)
	{
		return WithOptions(new AobScanOptions(protectionFlags, Options.AlignmentMethod, Options.AlignmentParameter));
	}

	/// <summary>Returns an equivalent builder with an explicit Cheat Engine alignment rule.</summary>
	/// <param name="method">The documented Cheat Engine alignment method.</param>
	/// <param name="parameter">The divisor or hexadecimal trailing-digit expression required by <paramref name="method" />.</param>
	/// <returns>A new immutable builder.</returns>
	public AobScanBuilder WithAlignment(FastScanMethod method, string? parameter)
	{
		return WithOptions(new AobScanOptions(Options.ProtectionFlags, method, parameter));
	}

	/// <summary>Selects an operation that succeeds only when exactly one AOB match exists.</summary>
	/// <returns>An immutable single-match terminal builder.</returns>
	public AobSingleMatchBuilder RequireSingle()
	{
		return new AobSingleMatchBuilder(RequireScanner(), BuildRequest(2));
	}

	/// <summary>Selects an operation that returns the first match or <see langword="null" /> when there is no match.</summary>
	/// <returns>An immutable first-match terminal builder.</returns>
	public AobFirstMatchBuilder FirstOrNone()
	{
		return new AobFirstMatchBuilder(RequireScanner(), BuildRequest(1));
	}

	/// <summary>Selects an operation that materializes no more than the requested number of matches.</summary>
	/// <param name="maximumResults">The positive maximum number of copied addresses to materialize.</param>
	/// <returns>An immutable bounded-result terminal builder.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="maximumResults" /> is zero or negative.</exception>
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
		return new AobScanRequest(Pattern, Options, maximumResults, Module, Range);
	}

	private AobScanBuilder WithOptions(AobScanOptions options)
	{
		// Validate and normalize at configuration time, not after the caller selected a terminal operation.
		AobScanRequest request = new(Pattern, options, 1, Module, Range);
		return new AobScanBuilder(_scanner, Pattern, request.Options, Module, Range);
	}
}
