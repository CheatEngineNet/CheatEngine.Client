using System.Globalization;

using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Enums;

namespace CheatEngine.Client.Core.Domains;

/// <summary>
///     Checks and writes the public scan options in Cheat Engine's own text, once for both scan routes: the AOB options
///     (<see cref="AobScanMapping.ToSdkOptions" />) and the value-scan first request (<c>ValueScanRequests</c>).
/// </summary>
/// <remarks>
///     The public values validate themselves when they are created; only a tampered value can be undefined. Each route
///     refuses one with <see cref="IsDefined(ScanProtectionFilter)" /> and <see cref="IsDefined(ScanAlignment)" />
///     before dispatch, so the translation never sees one; an undefined requirement or alignment writes nothing.
/// </remarks>
internal static class ScanOptionTranslation
{
	/// <summary>Gets whether every requirement of a filter is defined; only tampering can make one undefined.</summary>
	/// <param name="protection">The filter.</param>
	/// <returns><see langword="true" /> when each of the three requirements is a defined value.</returns>
	internal static bool IsDefined(ScanProtectionFilter protection)
	{
		return Enum.IsDefined(protection.Executable) && Enum.IsDefined(protection.CopyOnWrite) &&
			   Enum.IsDefined(protection.Writable);
	}

	/// <summary>Gets whether an alignment rule is one its factories can produce.</summary>
	/// <param name="alignment">The rule.</param>
	/// <returns><see langword="true" /> for no alignment, a positive divisor alone or non-empty digits alone.</returns>
	internal static bool IsDefined(ScanAlignment alignment)
	{
		return alignment.Mode switch
		{
			ScanAlignmentMode.None => alignment is { Divisor: 0, Digits: null },
			ScanAlignmentMode.AlignedTo => alignment is { Divisor: > 0, Digits: null },
			ScanAlignmentMode.LastDigits => alignment is { Divisor: 0, Digits.Length: > 0 },
			_ => false
		};
	}

	/// <summary>Writes a protection filter in Cheat Engine's clause grammar.</summary>
	/// <param name="protection">The filter.</param>
	/// <returns>
	///     The clauses in Cheat Engine's order <c>X</c>, <c>C</c>, <c>W</c> (<c>+</c> required, <c>-</c> excluded,
	///     <c>*</c> either); the empty string, which CheatEngine.SDK documents as Cheat Engine's "find everything"
	///     value, when every attribute is unspecified.
	/// </returns>
	internal static string ToProtectionText(ScanProtectionFilter protection)
	{
		Span<char> text = stackalloc char[6];
		int written = 0;
		AppendClause(text, ref written, protection.Executable, 'X');
		AppendClause(text, ref written, protection.CopyOnWrite, 'C');
		AppendClause(text, ref written, protection.Writable, 'W');
		return written == 0 ? string.Empty : new string(text[..written]);
	}

	/// <summary>Writes an alignment rule as Cheat Engine's fast-scan method and parameter.</summary>
	/// <param name="alignment">The rule.</param>
	/// <param name="unalignedParameter">
	///     The parameter the route sends without alignment: the AOB options omit it (<see langword="null" />), and the
	///     positional value-scan request sends the empty string.
	/// </param>
	/// <returns>
	///     The method and its parameter: the decimal divisor for <see cref="ScanAlignmentMode.AlignedTo" />, the
	///     upper-case digits for <see cref="ScanAlignmentMode.LastDigits" />, and <paramref name="unalignedParameter" />
	///     for <see cref="FastScanMethod.NotAligned" />.
	/// </returns>
	internal static (FastScanMethod Method, string? Parameter) ToFastScan(ScanAlignment alignment,
		string? unalignedParameter)
	{
		return alignment.Mode switch
		{
			ScanAlignmentMode.AlignedTo => (FastScanMethod.Aligned,
				alignment.Divisor.ToString(CultureInfo.InvariantCulture)),
			ScanAlignmentMode.LastDigits => (FastScanMethod.LastDigits, alignment.Digits ?? unalignedParameter),
			_ => (FastScanMethod.NotAligned, unalignedParameter)
		};
	}

	private static void AppendClause(Span<char> text, ref int written, ScanProtectionRequirement requirement,
		char flag)
	{
		char mode = requirement switch
		{
			ScanProtectionRequirement.Required => '+',
			ScanProtectionRequirement.Excluded => '-',
			ScanProtectionRequirement.Any => '*',
			_ => '\0'
		};
		if (mode == '\0')
		{
			return;
		}

		text[written++] = mode;
		text[written++] = flag;
	}
}
