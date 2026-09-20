namespace CheatEngine.Client.Scanning;

/// <summary>Describes the conservative Client-managed lifecycle of one value-scan session.</summary>
/// <remarks>
///     <para>
///         The successful path is <see cref="Created" />, <see cref="Scanning" />, then
///         <see cref="ResultsReady" />. A subsequent scan transitions from <see cref="ResultsReady" /> through
///         <see cref="Scanning" /> again. No SDK object or Lua handle is represented by this value.
///     </para>
///     <para>
///         <see cref="Invalidated" /> is deliberately conservative. It means a Cheat Engine operation began but failed
///         before
///         the Client could establish a safe next state; the session must be reset or disposed rather than reused
///         speculatively.
///     </para>
/// </remarks>
public enum ValueScanSessionState
{
	/// <summary>The session was created and has no readable result set.</summary>
	Created = 0,

	/// <summary>A first or subsequent scan was accepted and has not yet completed.</summary>
	Scanning = 1,

	/// <summary>The scan completed and its copied-result view is available for bounded reads.</summary>
	ResultsReady = 2,

	/// <summary>A started operation did not establish a safe continuation state.</summary>
	Invalidated = 3,

	/// <summary>The session has released its Client-owned resources and accepts no further operation.</summary>
	Disposed = 4
}
