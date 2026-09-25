namespace CheatEngine.Client.Scanning;

/// <summary>The fluent entry points of the AOB domain, bound to a scoped <see cref="IPatternScanner" />.</summary>
/// <remarks>
///     An AOB builder always starts from the scanner that runs its terminal operations, usually <c>client.Patterns</c>
///     of an activation-scoped <see cref="ICheatEngineClient" />: it is never created unbound and never rebound.
/// </remarks>
public static class CheatEngineAobFluentExtensions
{
	/// <summary>Starts an AOB scan bound to the supplied scoped scanner.</summary>
	/// <param name="scanner">The scoped scanner used by terminal operations.</param>
	/// <param name="pattern">The AOB pattern text to validate and normalize.</param>
	/// <returns>An immutable AOB scan builder bound to <paramref name="scanner" />.</returns>
	/// <exception cref="ArgumentNullException">
	///     <paramref name="scanner" /> or <paramref name="pattern" /> is <see langword="null" />.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="pattern" /> is empty or has an invalid token.</exception>
	public static AobScanBuilder Aob(this IPatternScanner scanner, string pattern)
	{
		ArgumentNullException.ThrowIfNull(scanner);
		return scanner.Aob(new AobPattern(pattern));
	}

	/// <summary>Starts an AOB scan of an already normalized pattern, bound to the supplied scoped scanner.</summary>
	/// <param name="scanner">The scoped scanner used by terminal operations.</param>
	/// <param name="pattern">The normalized AOB pattern, for example from <see cref="AobPattern.TryParse" />.</param>
	/// <returns>An immutable AOB scan builder bound to <paramref name="scanner" />.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="scanner" /> is <see langword="null" />.</exception>
	/// <exception cref="ArgumentException"><paramref name="pattern" /> is the empty default value.</exception>
	public static AobScanBuilder Aob(this IPatternScanner scanner, AobPattern pattern)
	{
		ArgumentNullException.ThrowIfNull(scanner);
		if (string.IsNullOrWhiteSpace(pattern.Value))
		{
			throw new ArgumentException("An AOB pattern must be non-empty.", nameof(pattern));
		}

		return new AobScanBuilder(scanner, pattern, default, ScanAlignment.None, null, null);
	}
}
