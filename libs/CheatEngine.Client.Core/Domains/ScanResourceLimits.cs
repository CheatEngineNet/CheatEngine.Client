namespace CheatEngine.Client.Core.Domains;

/// <summary>The fixed managed buffer limits of the scan routes that copy into a caller-sized destination.</summary>
/// <remarks>
///     A limit bounds the managed memory one call can claim, whatever a request asks for. It is a Client choice, not a
///     Cheat Engine or CheatEngine.SDK limit, and it never bounds Cheat Engine's own scan work.
/// </remarks>
internal static class ScanResourceLimits
{
	/// <summary>
	///     The largest destination of the bounded AOB route: <c>AobScanRequest.MaximumResults + 1</c> addresses, capped
	///     here. The extra slot proves truncation, so the route copies at most <c>MaximumPatternMatches - 1</c> addresses.
	/// </summary>
	/// <remarks>
	///     65,536 addresses are 512 KiB; CheatEngine.SDK stages them in a pooled buffer of the same length, so the call's
	///     managed peak stays near 1 MiB.
	/// </remarks>
	internal const int MaximumPatternMatches = 65_536;
}
