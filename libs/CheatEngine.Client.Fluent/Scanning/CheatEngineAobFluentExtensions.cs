namespace CheatEngine.Client.Scanning;

/// <summary>Starts immutable, handle-free AOB scans from a scoped pattern-scanner contract.</summary>
public static class CheatEngineAobFluentExtensions
{
	/// <summary>Starts an AOB scan bound to the supplied scoped scanner.</summary>
	/// <param name="scanner">The scoped scanner used by terminal operations.</param>
	/// <param name="pattern">The AOB pattern to validate and normalize.</param>
	/// <returns>An immutable AOB scan builder.</returns>
	public static AobScanBuilder Aob(this IPatternScanner scanner, string pattern)
	{
		ArgumentNullException.ThrowIfNull(scanner);
		return new AobScanBuilder(scanner, new AobPattern(pattern), default, ScanAlignment.None, null, null);
	}

	/// <summary>Starts an AOB scan using the pattern scanner of a scoped Cheat Engine client.</summary>
	/// <param name="client">The scoped Cheat Engine client.</param>
	/// <param name="pattern">The AOB pattern to validate and normalize.</param>
	/// <returns>An immutable AOB scan builder.</returns>
	public static AobScanBuilder Aob(this ICheatEngineClient client, string pattern)
	{
		ArgumentNullException.ThrowIfNull(client);
		return client.Patterns.Aob(pattern);
	}
}
