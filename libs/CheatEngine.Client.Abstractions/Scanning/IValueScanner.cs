using CheatEngine.Client.Results;

namespace CheatEngine.Client.Scanning;

/// <summary>Creates explicitly owned value-scan sessions.</summary>
public interface IValueScanner
{
	/// <summary>Tries to create a value-scan session for the current activation.</summary>
	public bool TryCreateSession(out IValueScanSession? session, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Creates a value-scan session or throws when the host capability is unavailable.</summary>
	public IValueScanSession CreateSession(CancellationToken cancellationToken = default);
}
