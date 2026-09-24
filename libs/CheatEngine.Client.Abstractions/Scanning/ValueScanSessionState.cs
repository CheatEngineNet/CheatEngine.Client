using System.Diagnostics.CodeAnalysis;

namespace CheatEngine.Client.Scanning;

/// <summary>The conservative lifecycle state of one value-scan session.</summary>
/// <remarks>
///     <para>
///         The successful path is <see cref="Created" />, then <see cref="ResultsReady" /> after each first or next scan;
///         a reset returns to <see cref="Created" />. The state is the one Cheat Engine's scan session reported after the
///         last operation; it is a copy that no Lua handle backs.
///     </para>
///     <para>
///         <see cref="Scanning" /> is observed only when a scan was started and the Client could not wait for it (a
///         cancellation between the two steps): the session then accepts nothing but its release, which asks Cheat Engine
///         to stop the scan. <see cref="Invalidated" /> means a Cheat Engine call began and did not establish a safe next
///         state (see <see cref="IValueScanSession.Invalidation" />): reset the session, or release it when the reset is
///         refused. A value this version does not define reads like <see cref="Unknown" />.
///     </para>
/// </remarks>
[Experimental(ClientExperimentalDiagnostics.ValueScans, UrlFormat = ClientExperimentalDiagnostics.UrlFormat)]
public enum ValueScanSessionState
{
	/// <summary>The state could not be established.</summary>
	Unknown = 0,

	/// <summary>The session has no scan result: it accepts a first scan.</summary>
	Created = 1,

	/// <summary>A scan was started and has not completed; only the release is accepted.</summary>
	Scanning = 2,

	/// <summary>The last scan completed: its results can be counted and read, and a next scan or a reset is accepted.</summary>
	ResultsReady = 3,

	/// <summary>A Cheat Engine call began without establishing a safe next state: reset or release the session.</summary>
	Invalidated = 4,

	/// <summary>The session's Cheat Engine objects were released or given up; no operation is accepted.</summary>
	Released = 5
}
