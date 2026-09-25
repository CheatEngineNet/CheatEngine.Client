using CheatEngine.SDK.Engine.Scanning.Values;

namespace CheatEngine.Client.Core.Domains;

/// <summary>
///     The one stop-confirmation check of a released Cheat Engine scan session, shared by the bounded AOB route
///     (<see cref="AobScanMapping.IsSessionReleaseConfirmed" />) and the value-scan sessions (<c>ValueScanMapping</c>).
/// </summary>
internal static class ScanTermination
{
	/// <summary>Returns whether no scan could still be running when the session's objects were released.</summary>
	/// <param name="termination">The SDK's termination status of the release.</param>
	/// <returns>
	///     <see langword="true" /> only when no stop was needed or the one cooperative stop was confirmed; an unconfirmed,
	///     refused or unrecognized stop is <see langword="false" />.
	/// </returns>
	internal static bool IsStopConfirmed(MemoryScanTerminationStatus termination)
	{
		return termination switch
		{
			MemoryScanTerminationStatus.NotRequired or MemoryScanTerminationStatus.Confirmed => true,
			MemoryScanTerminationStatus.Unknown or MemoryScanTerminationStatus.WaitTimedOut
				or MemoryScanTerminationStatus.TerminateFailed or MemoryScanTerminationStatus.WaitFailed
				or MemoryScanTerminationStatus.NotInvoked => false,
			_ => false
		};
	}
}
