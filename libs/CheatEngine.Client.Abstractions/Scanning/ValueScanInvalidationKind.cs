using System.Diagnostics.CodeAnalysis;

namespace CheatEngine.Client.Scanning;

/// <summary>Why a value-scan session was invalidated, as <see cref="IValueScanSession.Invalidation" /> reports it.</summary>
/// <remarks>A value this version does not define reads like <see cref="Unknown" />.</remarks>
[Experimental(ClientExperimentalDiagnostics.ValueScans, UrlFormat = ClientExperimentalDiagnostics.UrlFormat)]
public enum ValueScanInvalidationKind
{
	/// <summary>The reason could not be established.</summary>
	Unknown = 0,

	/// <summary>The session is not invalidated.</summary>
	None = 1,

	/// <summary>A Cheat Engine call of the scan, wait, reset or result sequence began and did not complete.</summary>
	HostCallFailed = 2,

	/// <summary>The Lua runtime that owns the session's Cheat Engine objects was replaced; only the release remains.</summary>
	RuntimeChanged = 3,

	/// <summary>
	///     Cheat Engine selected another process, or another incarnation of the same process identifier, than the one the
	///     session was created for; only the release remains.
	/// </summary>
	TargetChanged = 4
}
