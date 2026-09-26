using System.Text;

namespace CheatEngine.Client.Scanning;

/// <summary>An immutable, normalized sequence of Cheat Engine AOB byte and wildcard tokens.</summary>
/// <remarks>
///     <para>
///         The client deliberately accepts the documented, portable subset of string-form <c>AOBScan</c>: hexadecimal
///         bytes and <c>??</c> wildcards. It canonicalizes hexadecimal digits to upper case and emits exactly one ASCII
///         space between tokens. This prevents an invalid token from reaching Cheat Engine while retaining CE as the
///         authority for matching semantics.
///     </para>
///     <para>
///         The byte-array overload of CE's Lua function has broader numeric coercion rules. It is not used by this
///         string-based client surface, so numeric values above 255 and non-integral values are intentionally not
///         accepted here.
///     </para>
/// </remarks>
public readonly record struct AobPattern
{
	private readonly string? _value;

	/// <summary>Creates and normalizes an AOB pattern.</summary>
	/// <param name="value">Hexadecimal byte tokens and <c>??</c> wildcard tokens.</param>
	/// <exception cref="ArgumentNullException"><paramref name="value" /> is <see langword="null" />.</exception>
	/// <exception cref="ArgumentException"><paramref name="value" /> is empty or has an invalid token.</exception>
	public AobPattern(string value)
	{
		ArgumentNullException.ThrowIfNull(value);
		if (!TryNormalize(value.AsSpan(), out string normalized, out int byteLength))
		{
			throw new ArgumentException(
				"An AOB pattern must contain only two-digit hexadecimal bytes or ?? wildcard tokens.", nameof(value));
		}

		_value = normalized;
		ByteLength = byteLength;
	}

	private AobPattern(string normalized, int byteLength)
	{
		_value = normalized;
		ByteLength = byteLength;
	}

	/// <summary>Gets the normalized Cheat Engine pattern text.</summary>
	/// <remarks><see cref="string.Empty" /> for the <see langword="default" /> value.</remarks>
	public string Value => _value ?? string.Empty;

	/// <summary>Gets the number of byte positions represented by the pattern.</summary>
	public int ByteLength
	{
		get;
	}

	/// <summary>Gets whether every byte position is a wildcard.</summary>
	public bool IsWildcardOnly
	{
		get
		{
			if (string.IsNullOrEmpty(Value))
			{
				return false;
			}

			for (int index = 0; index < Value.Length; index += 3)
			{
				if (Value[index] != '?' || Value[index + 1] != '?')
				{
					return false;
				}
			}

			return true;
		}
	}

	/// <summary>Attempts to parse and normalize an AOB pattern without throwing for malformed text.</summary>
	/// <param name="value">The candidate pattern text, or <see langword="null" />.</param>
	/// <param name="pattern">The normalized pattern when this method returns <see langword="true" />.</param>
	/// <returns><see langword="true" /> when <paramref name="value" /> is a valid documented string-form AOB pattern.</returns>
	public static bool TryParse(string? value, out AobPattern pattern)
	{
		if (value is not null && TryNormalize(value.AsSpan(), out string normalized, out int byteLength))
		{
			pattern = new AobPattern(normalized, byteLength);
			return true;
		}

		pattern = default;
		return false;
	}

	/// <summary>Formats the pattern as normalized Cheat Engine text.</summary>
	/// <returns>
	///     <see cref="Value" />: the normalized text, or an empty string for the <see langword="default" /> value.
	/// </returns>
	public override string ToString()
	{
		return Value;
	}

	private static bool TryNormalize(ReadOnlySpan<char> value, out string normalized, out int byteLength)
	{
		StringBuilder builder = new(value.Length);
		int index = 0;
		byteLength = 0;

		while (TryMoveToNextToken(value, ref index))
		{
			if (!TryReadNormalizedToken(value, ref index, out char high, out char low))
			{
				normalized = string.Empty;
				byteLength = 0;
				return false;
			}

			AppendToken(builder, high, low);
			byteLength++;
		}

		if (byteLength != 0)
		{
			normalized = builder.ToString();
			return true;
		}

		normalized = string.Empty;
		return false;
	}

	private static bool TryMoveToNextToken(ReadOnlySpan<char> value, ref int index)
	{
		while (index < value.Length && char.IsWhiteSpace(value[index]))
		{
			index++;
		}

		return index < value.Length;
	}

	private static bool TryReadNormalizedToken(ReadOnlySpan<char> value, ref int index, out char high, out char low)
	{
		if (index + 1 >= value.Length || char.IsWhiteSpace(value[index + 1]))
		{
			high = default;
			low = default;
			return false;
		}

		high = value[index++];
		low = value[index++];
		if (high == '?' && low == '?')
		{
			return true;
		}

		return TryGetHexDigit(high, out high) && TryGetHexDigit(low, out low);
	}

	private static void AppendToken(StringBuilder builder, char high, char low)
	{
		if (builder.Length != 0)
		{
			builder.Append(' ');
		}

		builder.Append(high);
		builder.Append(low);
	}

	private static bool TryGetHexDigit(char value, out char normalized)
	{
		if (value is (>= '0' and <= '9') or (>= 'A' and <= 'F'))
		{
			normalized = value;
			return true;
		}

		if (value is >= 'a' and <= 'f')
		{
			normalized = (char) (value - ('a' - 'A'));
			return true;
		}

		normalized = default;
		return false;
	}
}
