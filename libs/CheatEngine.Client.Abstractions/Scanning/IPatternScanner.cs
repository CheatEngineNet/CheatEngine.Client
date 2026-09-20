using CheatEngine.Client.Results;

namespace CheatEngine.Client.Scanning;

/// <summary>Runs bounded AOB scans and copies all returned addresses before releasing SDK-owned objects.</summary>
public interface IPatternScanner
{
	/// <summary>Tries to run one bounded pattern scan.</summary>
	public bool TryScan(AobScanRequest request, out AobScanResult result, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Runs one bounded pattern scan or throws when the operation fails.</summary>
	public AobScanResult Scan(AobScanRequest request, CancellationToken cancellationToken = default);
}
