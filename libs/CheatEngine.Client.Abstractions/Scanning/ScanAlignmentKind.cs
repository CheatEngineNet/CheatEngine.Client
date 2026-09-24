namespace CheatEngine.Client.Scanning;

/// <summary>The alignment rule a scan applies to candidate addresses.</summary>
/// <remarks>
///     An option, not an outcome: <see cref="None" /> (zero) is the valid default that checks every address.
/// </remarks>
public enum ScanAlignmentKind
{
	/// <summary>Every address is checked (Cheat Engine's <c>fsmNotAligned</c>).</summary>
	None = 0,

	/// <summary>
	///     Only addresses divisible by <see cref="ScanAlignment.Divisor" /> are checked (Cheat Engine's
	///     <c>fsmAligned</c>).
	/// </summary>
	AlignedTo = 1,

	/// <summary>
	///     Only addresses whose hexadecimal text ends with <see cref="ScanAlignment.Digits" /> are checked (Cheat Engine's
	///     <c>fsmLastDigits</c>).
	/// </summary>
	LastDigits = 2
}
