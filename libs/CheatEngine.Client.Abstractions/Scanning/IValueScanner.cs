using CheatEngine.Client.Results;

namespace CheatEngine.Client.Scanning;

/// <summary>Creates explicitly owned value-scan sessions.</summary>
/// <remarks>
///     <b>Call-only.</b> The Client implements this interface and applications call it. A minor release can add members
///     to it, so implement it only in a test double.
/// </remarks>
public interface IValueScanner
{
	/// <summary>Tries to create a value-scan session for the current activation.</summary>
	public bool TryCreateSession(out IValueScanSession? session, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Creates a value-scan session or throws when the host capability is unavailable.</summary>
	public IValueScanSession CreateSession(CancellationToken cancellationToken = default);
}
