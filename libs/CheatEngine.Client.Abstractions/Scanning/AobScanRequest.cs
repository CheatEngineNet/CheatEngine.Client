using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Scanning.Aob;

namespace CheatEngine.Client.Scanning;

/// <summary>An immutable AOB scan request with an explicit managed materialization limit.</summary>
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

	/// <summary>Gets the maximum number of managed matches the caller permits.</summary>
	public int MaximumResults
	{
		get;
	}

	/// <summary>Gets the optional module whose copied address range filters the results.</summary>
	public ModuleName? Module
	{
		get;
	}

	/// <summary>Gets the optional inclusive range that filters copied address results.</summary>
	public AobScanRange? Range
	{
		get;
	}
}
