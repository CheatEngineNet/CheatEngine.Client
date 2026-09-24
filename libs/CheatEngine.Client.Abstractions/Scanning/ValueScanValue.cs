using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace CheatEngine.Client.Scanning;

/// <summary>A typed value that a value scan compares, with the exact text passed to Cheat Engine.</summary>
/// <remarks>
///     <para>
///         Create a value with a typed factory: it chooses the <see cref="ValueScanValueType" /> and formats the text with
///         the invariant culture. Integers are decimal; <see cref="FromSingle" /> and <see cref="FromDouble" /> use the
///         shortest round-trip form with a <c>.</c> separator; <see cref="FromBytes" /> writes each byte as two
///         hexadecimal digits separated by spaces. The wider integer factories take signed values: pass an unsigned value
///         as its bit-identical signed value, for example <c>unchecked((int)value)</c>. An exact comparison matches the
///         same bits either way; an ordered comparison follows Cheat Engine's own rules.
///     </para>
///     <para>
///         A <see langword="default" /> value has no <see cref="Text" /> and every scan request refuses it. The text is user
///         data: log it only on an explicit opt-in.
///     </para>
/// </remarks>
[Experimental(ClientExperimentalDiagnostics.ValueScans, UrlFormat = ClientExperimentalDiagnostics.UrlFormat)]
public readonly record struct ValueScanValue
{
	private ValueScanValue(ValueScanValueType type, string text)
	{
		Type = type;
		Text = text;
	}

	/// <summary>Gets the type Cheat Engine compares.</summary>
	public ValueScanValueType Type
	{
		get;
	}

	/// <summary>Gets the exact text passed to Cheat Engine, or <see langword="null" /> for a default value.</summary>
	public string? Text
	{
		get;
	}

	/// <summary>Gets whether the value is a numeric type, which ordered comparisons and unknown initial values accept.</summary>
	public bool IsNumeric => Type is >= ValueScanValueType.Integer8 and <= ValueScanValueType.DoubleFloat;

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

	/// <summary>Creates a single-precision value.</summary>
	/// <param name="value">A finite value.</param>
	/// <returns>A <see cref="ValueScanValueType.SingleFloat" /> value.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="value" /> is not finite.</exception>
	public static ValueScanValue FromSingle(float value)
	{
		if (!float.IsFinite(value))
		{
			throw new ArgumentOutOfRangeException(nameof(value), value, "A scanned floating-point value must be finite.");
		}

		return new ValueScanValue(ValueScanValueType.SingleFloat, value.ToString("R", CultureInfo.InvariantCulture));
	}

	/// <summary>Creates a double-precision value.</summary>
	/// <param name="value">A finite value.</param>
	/// <returns>A <see cref="ValueScanValueType.DoubleFloat" /> value.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="value" /> is not finite.</exception>
	public static ValueScanValue FromDouble(double value)
	{
		if (!double.IsFinite(value))
		{
			throw new ArgumentOutOfRangeException(nameof(value), value, "A scanned floating-point value must be finite.");
		}

		return new ValueScanValue(ValueScanValueType.DoubleFloat, value.ToString("R", CultureInfo.InvariantCulture));
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
}
