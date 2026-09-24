using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Scanning.Values;

namespace CheatEngine.Client.Scanning;

/// <summary>A main-thread-bound, explicitly disposable high-level value-scan session.</summary>
/// <remarks>
///     <para>
///         <b>Call-only.</b> The Client implements this interface and applications call it. A minor release can add
///         members to it, so implement it only in a test double.
///     </para>
///     <para>
///         The session exposes a Client-managed <see cref="ValueScanSessionState" /> rather than the SDK's raw scan
///         state. Its operations complete Cheat Engine's required wait-and-initialize sequence before publishing copied
///         results, and never expose MemScan, FoundList, Lua, or ownership wrappers.
///     </para>
/// </remarks>
public interface IValueScanSession : IDisposable
{
	/// <summary>Gets the session's current conservative scan state.</summary>
	public ValueScanSessionState State
	{
		get;
	}

	/// <summary>Runs a first scan and prepares its results for reading.</summary>
	public bool TryStart(FirstScanRequest request, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Runs a first scan or throws when it fails.</summary>
	public void Start(FirstScanRequest request, CancellationToken cancellationToken = default);

	/// <summary>Runs a next scan and prepares its results for reading.</summary>
	public bool TryRunNextScan(NextScanRequest request, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Runs a next scan or throws when it fails.</summary>
	public void RunNextScan(NextScanRequest request, CancellationToken cancellationToken = default);

	/// <summary>Resets the scan session to its initial state.</summary>
	public bool TryReset(out CheatEngineFailure failure, CancellationToken cancellationToken = default);

	/// <summary>Resets the scan session or throws when it fails.</summary>
	public void Reset(CancellationToken cancellationToken = default);

	/// <summary>Gets the current result count after results are ready.</summary>
	public bool TryGetResultCount(out ulong resultCount, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Gets the current result count or throws when results are not ready.</summary>
	public ulong GetResultCount(CancellationToken cancellationToken = default);

	/// <summary>Copies a bounded page of current results after results are ready.</summary>
	public bool TryRead(ValueScanReadRequest request, out ValueScanPage page, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Copies a bounded page or throws when the session is not ready.</summary>
	public ValueScanPage Read(ValueScanReadRequest request, CancellationToken cancellationToken = default);
}
