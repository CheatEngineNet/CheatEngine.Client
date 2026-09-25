using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace CheatEngine.Client.Scanning;

/// <summary>A typed value that a value scan compares, with the exact text passed to Cheat Engine.</summary>
/// <remarks>
///     <para>
///         Create a value with a typed factory: it chooses the <see cref="ValueScanValueType" /> and formats the text with
///         the invariant culture. Integers are decimal; <see cref="FromBytes" /> writes each byte as two hexadecimal digits
///         separated by spaces. The wider integer factories take signed values: pass an unsigned value as its
///         bit-identical signed value, for example <c>unchecked((int)value)</c>. An exact comparison matches the same bits
///         either way; an ordered comparison follows Cheat Engine's own rules.
///     </para>
///     <para>
///         <b>Floating-point precision.</b> <see cref="FromSingle" /> and <see cref="FromDouble" /> write the value in
///         fixed-point notation, never in exponent notation, rounded to the number of decimals the caller passes, with a
///         <c>.</c> separator: <c>FromDouble(100, 2)</c> is <c>100.00</c>. Cheat Engine's rounded exact comparison takes
///         its precision from the digits of that text: its Lua documentation (<c>rtRounded</c>) states that <c>3</c>
///         matches 3.0 to 3.4999 and <c>3.0</c> matches 3.00 to 3.0499, and does not say whether a value just below the
///         text (2.6 for <c>3</c>) matches as well, as ordinary rounding to that many decimals would. Choose the decimals
///         the scan needs, knowing that each one fewer widens the match tenfold. No Client receipt covers this tolerance
///         yet: Q25 records which of the two rules the host applies.
///     </para>
///     <para>
///         A <see langword="default" /> value has no <see cref="Text" />: every scan request factory throws an
///         <see cref="ArgumentException" /> for it. The text is user data: log it only on an explicit opt-in.
///     </para>
/// </remarks>
[Experimental(ClientExperimentalDiagnostics.ValueScans, UrlFormat = ClientExperimentalDiagnostics.UrlFormat)]
public readonly record struct ValueScanValue
{
	private const int MaximumFloatDecimals = 15;

	private ValueScanValue(ValueScanValueType valueType, string text)
	{
		ValueType = valueType;
		Text = text;
	}

	/// <summary>
	///     Gets the type Cheat Engine compares; a first scan takes its
	///     <see cref="ValueScanFirstRequest.ValueType" /> from its value.
	/// </summary>
	public ValueScanValueType ValueType
	{
		get;
	}

	/// <summary>Gets the exact text passed to Cheat Engine, or <see langword="null" /> for a default value.</summary>
	public string? Text
	{
		get;
	}

	/// <summary>Gets whether the value is a numeric type, which ordered comparisons and unknown initial values accept.</summary>
	public bool IsNumeric => ValueType is >= ValueScanValueType.Integer8 and <= ValueScanValueType.DoubleFloat;

	/// <summary>Creates a one-byte value.</summary>
	/// <param name="value">The value.</param>
	/// <returns>A <see cref="ValueScanValueType.Integer8" /> value.</returns>
	public static ValueScanValue FromByte(byte value)
	{
		return new ValueScanValue(ValueScanValueType.Integer8, value.ToString(CultureInfo.InvariantCulture));
	}

	/// <summary>Creates a two-byte value.</summary>
	/// <param name="value">The value.</param>
	/// <returns>A <see cref="ValueScanValueType.Integer16" /> value.</returns>
	public static ValueScanValue FromInt16(short value)
	{
		return new ValueScanValue(ValueScanValueType.Integer16, value.ToString(CultureInfo.InvariantCulture));
	}

	/// <summary>Creates a four-byte value.</summary>
	/// <param name="value">The value.</param>
	/// <returns>A <see cref="ValueScanValueType.Integer32" /> value.</returns>
	public static ValueScanValue FromInt32(int value)
	{
		return new ValueScanValue(ValueScanValueType.Integer32, value.ToString(CultureInfo.InvariantCulture));
	}

	/// <summary>Creates an eight-byte value.</summary>
	/// <param name="value">The value.</param>
	/// <returns>A <see cref="ValueScanValueType.Integer64" /> value.</returns>
	public static ValueScanValue FromInt64(long value)
	{
		return new ValueScanValue(ValueScanValueType.Integer64, value.ToString(CultureInfo.InvariantCulture));
	}

	/// <summary>Creates a single-precision value, written with a fixed number of decimals.</summary>
	/// <param name="value">A finite value.</param>
	/// <param name="decimals">
	///     The number of decimals, from 0 to 15, which Cheat Engine's rounded exact comparison uses as its precision.
	/// </param>
	/// <returns>A <see cref="ValueScanValueType.SingleFloat" /> value.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="value" /> is not finite, or <paramref name="decimals" /> is outside 0 to 15.
	/// </exception>
	public static ValueScanValue FromSingle(float value, int decimals)
	{
		if (!float.IsFinite(value))
		{
			throw new ArgumentOutOfRangeException(nameof(value), value, "A scanned floating-point value must be finite.");
		}

		return new ValueScanValue(ValueScanValueType.SingleFloat,
			value.ToString(FixedPointFormat(decimals), CultureInfo.InvariantCulture));
	}

	/// <summary>Creates a double-precision value, written with a fixed number of decimals.</summary>
	/// <param name="value">A finite value.</param>
	/// <param name="decimals">
	///     The number of decimals, from 0 to 15, which Cheat Engine's rounded exact comparison uses as its precision.
	/// </param>
	/// <returns>A <see cref="ValueScanValueType.DoubleFloat" /> value.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="value" /> is not finite, or <paramref name="decimals" /> is outside 0 to 15.
	/// </exception>
	public static ValueScanValue FromDouble(double value, int decimals)
	{
		if (!double.IsFinite(value))
		{
			throw new ArgumentOutOfRangeException(nameof(value), value, "A scanned floating-point value must be finite.");
		}

		return new ValueScanValue(ValueScanValueType.DoubleFloat,
			value.ToString(FixedPointFormat(decimals), CultureInfo.InvariantCulture));
	}

	/// <summary>Creates a case-sensitive UTF-8 text.</summary>
	/// <param name="text">The non-empty text.</param>
	/// <returns>A <see cref="ValueScanValueType.Utf8String" /> value.</returns>
	/// <exception cref="ArgumentException"><paramref name="text" /> is <see langword="null" /> or empty.</exception>
	public static ValueScanValue FromUtf8String(string text)
	{
		ArgumentException.ThrowIfNullOrEmpty(text);
		return new ValueScanValue(ValueScanValueType.Utf8String, text);
	}

	/// <summary>Creates a case-sensitive UTF-16 text.</summary>
	/// <param name="text">The non-empty text.</param>
	/// <returns>A <see cref="ValueScanValueType.Utf16String" /> value.</returns>
	/// <exception cref="ArgumentException"><paramref name="text" /> is <see langword="null" /> or empty.</exception>
	public static ValueScanValue FromUtf16String(string text)
	{
		ArgumentException.ThrowIfNullOrEmpty(text);
		return new ValueScanValue(ValueScanValueType.Utf16String, text);
	}

	/// <summary>Creates an exact byte sequence.</summary>
	/// <param name="bytes">The non-empty bytes, in target memory order.</param>
	/// <returns>A <see cref="ValueScanValueType.ByteArray" /> value.</returns>
	/// <exception cref="ArgumentException"><paramref name="bytes" /> is empty.</exception>
	public static ValueScanValue FromBytes(ReadOnlySpan<byte> bytes)
	{
		if (bytes.IsEmpty)
		{
			throw new ArgumentException("A scanned byte sequence must not be empty.", nameof(bytes));
		}

		StringBuilder text = new((bytes.Length * 3) - 1);
		for (int index = 0; index < bytes.Length; index++)
		{
			if (index > 0)
			{
				_ = text.Append(' ');
			}

			_ = text.Append(bytes[index].ToString("X2", CultureInfo.InvariantCulture));
		}

		return new ValueScanValue(ValueScanValueType.ByteArray, text.ToString());
	}

	/// <summary>Returns the fixed-point format of a number of decimals, which is never exponent notation.</summary>
	private static string FixedPointFormat(int decimals)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(decimals);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(decimals, MaximumFloatDecimals);
		return "F" + decimals.ToString(CultureInfo.InvariantCulture);
	}
}
