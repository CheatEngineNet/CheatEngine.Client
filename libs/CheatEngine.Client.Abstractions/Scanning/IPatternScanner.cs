using CheatEngine.Client.Results;

namespace CheatEngine.Client.Scanning;

/// <summary>Runs global AOB scans, then copies post-filtered addresses before releasing SDK-owned objects.</summary>
public interface IPatternScanner
{
	/// <summary>Tries to run one scan with managed post-filtered result materialization.</summary>
	public bool TryScan(AobScanRequest request, out AobScanResult result, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Runs one scan with managed post-filtered result materialization or throws when it fails.</summary>
	public AobScanResult Scan(AobScanRequest request, CancellationToken cancellationToken = default);
}
