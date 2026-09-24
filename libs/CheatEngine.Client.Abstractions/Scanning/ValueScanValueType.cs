using System.Diagnostics.CodeAnalysis;

namespace CheatEngine.Client.Scanning;

/// <summary>The kind of value a value scan compares, in Cheat Engine's terms.</summary>
/// <remarks>
///     A numeric type selects the width Cheat Engine compares (one, two, four or eight bytes, or an IEEE single or double);
///     the text types select a UTF-8 or UTF-16 string scan, and <see cref="ByteArray" /> an exact byte sequence.
/// </remarks>
[Experimental(ClientExperimentalDiagnostics.ValueScans, UrlFormat = ClientExperimentalDiagnostics.UrlFormat)]
public enum ValueScanValueType
{
	/// <summary>A one-byte integer (Cheat Engine <c>vtByte</c>).</summary>
	Integer8 = 0,

	/// <summary>A two-byte integer (Cheat Engine <c>vtWord</c>).</summary>
	Integer16 = 1,

	/// <summary>A four-byte integer (Cheat Engine <c>vtDword</c>).</summary>
	Integer32 = 2,

	/// <summary>An eight-byte integer (Cheat Engine <c>vtQword</c>).</summary>
	Integer64 = 3,

	/// <summary>An IEEE 754 single-precision value (Cheat Engine <c>vtSingle</c>).</summary>
	SingleFloat = 4,

	/// <summary>An IEEE 754 double-precision value (Cheat Engine <c>vtDouble</c>).</summary>
	DoubleFloat = 5,

	/// <summary>A UTF-8 text (Cheat Engine <c>vtString</c>).</summary>
	Utf8String = 6,

	/// <summary>A UTF-16 text (Cheat Engine <c>vtString</c> with its Unicode option).</summary>
	Utf16String = 7,

	/// <summary>An exact sequence of bytes (Cheat Engine <c>vtByteArray</c>).</summary>
	ByteArray = 8
}
